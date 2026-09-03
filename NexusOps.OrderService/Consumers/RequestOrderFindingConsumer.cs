using MassTransit;
using NexusOps.Contracts.Dtos;
using NexusOps.Contracts.Messages;
using NexusOps.OrderService.Data;

namespace NexusOps.OrderService.Consumers;

/// <summary>
/// Answers the fan-out coordinator's order lookup for a root-cause investigation. Responds rather
/// than publishes — this leg of the fan-out is request/response (saga-message-contracts.md, Leg 3).
/// </summary>
public sealed class RequestOrderFindingConsumer(TimeProvider timeProvider, OrderMutationOverlay overlay) : IConsumer<RequestOrderFinding>
{
    public Task Consume(ConsumeContext<RequestOrderFinding> context)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var order = OrderStore.GetOrders(today).FirstOrDefault(o =>
            string.Equals(o.OrderId, context.Message.OrderId, StringComparison.OrdinalIgnoreCase));

        if (order is null)
        {
            return context.RespondAsync(new OrderFindingReported(
                context.Message.CorrelationId, SourceFindingStatus.NotFound, null));
        }

        order = order.ApplyOverlay(overlay);

        var summary = new OrderSummary(
            OrderId: order.OrderId,
            CustomerId: order.CustomerId,
            Status: order.Status.ToString().ToLowerInvariant(),
            TotalAmount: order.TotalAmount,
            ExpectedDelivery: order.ExpectedDelivery,
            ActualDelivery: order.ActualDelivery,
            LineItems: order.LineItems
                .Select(li => new OrderLineItem(li.Sku, li.ProductName, li.Quantity, li.UnitPrice))
                .ToArray(),
            RefundedAmount: order.RefundedAmount);

        return context.RespondAsync(new OrderFindingReported(
            context.Message.CorrelationId, SourceFindingStatus.Succeeded, summary));
    }
}
