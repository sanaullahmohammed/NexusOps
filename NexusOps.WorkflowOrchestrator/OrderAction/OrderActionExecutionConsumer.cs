using MassTransit;
using NexusOps.Contracts.Dtos;
using NexusOps.Contracts.Messages;

namespace NexusOps.WorkflowOrchestrator.OrderAction;

/// <summary>
/// Does the actual execution work the saga itself never blocks on: a refund is one dependency
/// (the order); a cancellation is two (the order, then the inventory it reserved) — and, if the
/// second fails after the first succeeded, a compensating reversal of the first (spec.md User
/// Story 4). Holds no state of its own; a crash mid-execution is covered by ordinary message
/// redelivery of <see cref="BeginOrderActionExecution"/>, backed by the saga's transactional
/// outbox (research.md Decision 6).
/// </summary>
public sealed class OrderActionExecutionConsumer(
    IRequestClient<ExecuteOrderMutation> orderMutationClient,
    IRequestClient<ExecuteInventoryRestock> inventoryRestockClient,
    IRequestClient<CompensateOrderMutation> compensateClient) : IConsumer<BeginOrderActionExecution>
{
    private static readonly RequestTimeout PerLegTimeout = RequestTimeout.After(s: 5);

    /// <summary>
    /// The inventory leg's outcome, distinguishing a *confirmed* failure (safe to compensate — the
    /// service told us definitively the restock didn't happen) from an *uncertain* one (a timeout:
    /// the request may still succeed after our client gave up waiting). Compensating on an uncertain
    /// outcome risks reverting the order while the restock lands anyway moments later — inventory
    /// shows restocked, order shows not-cancelled, an inconsistent state worse than an honest
    /// "couldn't confirm" (code review finding).
    /// </summary>
    private readonly record struct InventoryLegResult(bool Success, bool Uncertain, string? FailureReason);

    public async Task Consume(ConsumeContext<BeginOrderActionExecution> context)
    {
        var correlationId = context.Message.CorrelationId;
        var orderId = context.Message.OrderId;
        var cancellationToken = context.CancellationToken;

        var mutation = await ExecuteOrderMutationAsync(correlationId, context.Message.ActionType, orderId, context.Message.Amount, cancellationToken);

        if (!mutation.Success)
        {
            await context.Publish(new OrderActionExecutionCompleted(
                correlationId, OrderActionExecutionOutcome.Failed, mutation.FailureReason ?? "The order could not be updated.", mutation.PriorStatus));
            return;
        }

        if (context.Message.ActionType != OrderActionType.Cancellation)
        {
            await context.Publish(new OrderActionExecutionCompleted(
                correlationId, OrderActionExecutionOutcome.Executed, $"Refund for order {orderId} executed.", mutation.PriorStatus));
            return;
        }

        var lines = mutation.LineItems.Select(li => new InventoryRestockLine(li.Sku, li.Quantity)).ToArray();
        var restock = await ExecuteInventoryRestockAsync(correlationId, orderId, lines, cancellationToken);

        if (restock.Success)
        {
            await context.Publish(new OrderActionExecutionCompleted(
                correlationId, OrderActionExecutionOutcome.Executed, $"Cancellation for order {orderId} executed; inventory released.", mutation.PriorStatus));
            return;
        }

        if (restock.Uncertain)
        {
            // Do NOT compensate: we don't know whether the restock will still land after our
            // timeout. Reverting the order now and having the restock succeed moments later would
            // leave inventory showing restocked stock for an order that isn't actually cancelled —
            // a worse, silently-inconsistent state than reporting this honestly as unconfirmed.
            await context.Publish(new OrderActionExecutionCompleted(
                correlationId,
                OrderActionExecutionOutcome.Failed,
                $"Cancellation for order {orderId}: the order was updated, but inventory release could not be confirmed ({restock.FailureReason}). " +
                "The order was NOT reverted, since the release may still complete. Manual reconciliation may be required.",
                mutation.PriorStatus));
            return;
        }

        // Inventory *confirmed* it didn't happen (a fault/exception, not a timeout) after the order
        // mutation already succeeded — safe to reverse it rather than leave the order showing a
        // cancellation the inventory data does not corroborate (FR-011).
        await CompensateOrderMutationAsync(correlationId, orderId, mutation.PriorStatus, cancellationToken);

        await context.Publish(new OrderActionExecutionCompleted(
            correlationId,
            OrderActionExecutionOutcome.FailedAndCompensated,
            $"Cancellation for order {orderId} could not complete (inventory release failed: {restock.FailureReason}); the order was reverted.",
            mutation.PriorStatus));
    }

    private async Task<OrderMutationExecuted> ExecuteOrderMutationAsync(
        Guid correlationId, OrderActionType actionType, string orderId, decimal? amount, CancellationToken cancellationToken)
    {
        try
        {
            var response = await orderMutationClient.GetResponse<OrderMutationExecuted>(
                new ExecuteOrderMutation(correlationId, actionType, orderId, amount), cancellationToken, PerLegTimeout);
            return response.Message;
        }
        catch (RequestTimeoutException)
        {
            return new OrderMutationExecuted(correlationId, false, "The order service timed out.", "unknown", []);
        }
        catch (Exception)
        {
            return new OrderMutationExecuted(correlationId, false, "The order service is unavailable.", "unknown", []);
        }
    }

    private async Task<InventoryLegResult> ExecuteInventoryRestockAsync(
        Guid correlationId, string orderId, InventoryRestockLine[] lines, CancellationToken cancellationToken)
    {
        try
        {
            var response = await inventoryRestockClient.GetResponse<InventoryRestockExecuted>(
                new ExecuteInventoryRestock(correlationId, orderId, lines), cancellationToken, PerLegTimeout);
            return new InventoryLegResult(response.Message.Success, Uncertain: false, response.Message.FailureReason);
        }
        catch (RequestTimeoutException)
        {
            return new InventoryLegResult(false, Uncertain: true, "the inventory service timed out");
        }
        catch (Exception)
        {
            return new InventoryLegResult(false, Uncertain: false, "the inventory service is unavailable");
        }
    }

    private async Task CompensateOrderMutationAsync(Guid correlationId, string orderId, string revertToStatus, CancellationToken cancellationToken)
    {
        try
        {
            await compensateClient.GetResponse<OrderMutationCompensated>(
                new CompensateOrderMutation(correlationId, orderId, revertToStatus), cancellationToken, PerLegTimeout);
        }
        catch (Exception)
        {
            // Best-effort: the outcome reported to the caller already reflects a failure regardless
            // (T070). A compensation that cannot even be attempted because OrderService is also down
            // is a rarer, harder failure this POC does not retry indefinitely.
        }
    }
}
