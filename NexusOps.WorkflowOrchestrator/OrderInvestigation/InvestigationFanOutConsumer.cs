using MassTransit;
using NexusOps.Contracts.Dtos;
using NexusOps.Contracts.Messages;

namespace NexusOps.WorkflowOrchestrator.OrderInvestigation;

/// <summary>
/// Does the actual fan-out work the saga itself never blocks on: three request/response calls to
/// the domain services, each bounded by its own timeout, publishing a <c>*FindingReported</c>
/// event for the saga no matter how each call resolves (research.md Decision 1). Holds no state
/// of its own -- a crash mid-fan-out is covered by ordinary message redelivery of
/// <see cref="BeginInvestigationFanOut"/>, and rerunning a read is always safe.
/// </summary>
public sealed class InvestigationFanOutConsumer(
    IRequestClient<RequestOrderFinding> orderClient,
    IRequestClient<RequestInventoryFinding> inventoryClient,
    IRequestClient<RequestProductFinding> productClient) : IConsumer<BeginInvestigationFanOut>
{
    private static readonly RequestTimeout PerSourceTimeout = RequestTimeout.After(s: 5);

    public async Task Consume(ConsumeContext<BeginInvestigationFanOut> context)
    {
        var correlationId = context.Message.CorrelationId;
        var orderId = context.Message.OrderId;
        var cancellationToken = context.CancellationToken;

        var orderFinding = await GetOrderFindingAsync(correlationId, orderId, cancellationToken);
        await context.Publish(orderFinding);

        if (orderFinding.Status != SourceFindingStatus.Succeeded || orderFinding.Order is null || orderFinding.Order.LineItems.Length == 0)
        {
            // Either the order itself couldn't be identified, or it has nothing to check against
            // Inventory/Product -- either way there are no SKUs to look up (spec.md Edge Cases:
            // "nothing to check" completes the investigation rather than hanging on it). A
            // confirmed NotFound propagates as NotFound (a completed finding, not a degraded
            // source); only a genuine Order-side failure (Unavailable/TimedOut) propagates as
            // Unavailable, since only that case means the source truly couldn't be consulted.
            var emptyStatus = orderFinding.Status switch
            {
                SourceFindingStatus.NotFound => SourceFindingStatus.NotFound,
                SourceFindingStatus.Succeeded => SourceFindingStatus.NotFound, // Succeeded but no line items
                _ => SourceFindingStatus.Unavailable
            };

            await context.Publish(new InventoryFindingReported(correlationId, emptyStatus, [], []));
            await context.Publish(new ProductFindingReported(correlationId, emptyStatus, [], []));
            return;
        }

        var skus = orderFinding.Order.LineItems.Select(li => li.Sku).Distinct().ToArray();

        var inventoryTask = GetInventoryFindingAsync(correlationId, skus, cancellationToken);
        var productTask = GetProductFindingAsync(correlationId, skus, cancellationToken);
        await Task.WhenAll(inventoryTask, productTask);

        await context.Publish(await inventoryTask);
        await context.Publish(await productTask);
    }

    private async Task<OrderFindingReported> GetOrderFindingAsync(Guid correlationId, string orderId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await orderClient.GetResponse<OrderFindingReported>(
                new RequestOrderFinding(correlationId, orderId), cancellationToken, PerSourceTimeout);
            return response.Message;
        }
        catch (RequestTimeoutException)
        {
            return new OrderFindingReported(correlationId, SourceFindingStatus.TimedOut, null);
        }
        catch (Exception)
        {
            return new OrderFindingReported(correlationId, SourceFindingStatus.Unavailable, null);
        }
    }

    private async Task<InventoryFindingReported> GetInventoryFindingAsync(Guid correlationId, string[] skus, CancellationToken cancellationToken)
    {
        try
        {
            var response = await inventoryClient.GetResponse<InventoryFindingReported>(
                new RequestInventoryFinding(correlationId, skus), cancellationToken, PerSourceTimeout);
            return response.Message;
        }
        catch (RequestTimeoutException)
        {
            return new InventoryFindingReported(correlationId, SourceFindingStatus.TimedOut, [], []);
        }
        catch (Exception)
        {
            return new InventoryFindingReported(correlationId, SourceFindingStatus.Unavailable, [], []);
        }
    }

    private async Task<ProductFindingReported> GetProductFindingAsync(Guid correlationId, string[] skus, CancellationToken cancellationToken)
    {
        try
        {
            var response = await productClient.GetResponse<ProductFindingReported>(
                new RequestProductFinding(correlationId, skus), cancellationToken, PerSourceTimeout);
            return response.Message;
        }
        catch (RequestTimeoutException)
        {
            return new ProductFindingReported(correlationId, SourceFindingStatus.TimedOut, [], []);
        }
        catch (Exception)
        {
            return new ProductFindingReported(correlationId, SourceFindingStatus.Unavailable, [], []);
        }
    }
}
