using MassTransit;
using NexusOps.Contracts.Dtos;
using NexusOps.Contracts.Messages;
using NexusOps.OrderService.Data;
using NexusOps.OrderService.Models;

namespace NexusOps.OrderService.Consumers;

/// <summary>
/// Executes an approved refund or cancellation against <see cref="OrderMutationOverlay"/>. One
/// consumer handles both action types — they differ only in target status (research.md Decision
/// 8). Never throws and never silently applies an ineligible mutation (spec.md FR-013): an order
/// already in a status incompatible with the requested action responds <c>Success: false</c> with
/// a reason, leaving the overlay untouched.
/// </summary>
public sealed class ExecuteOrderMutationConsumer(TimeProvider timeProvider, OrderMutationOverlay overlay) : IConsumer<ExecuteOrderMutation>
{
    public Task Consume(ConsumeContext<ExecuteOrderMutation> context)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var order = OrderStore.GetOrders(today).FirstOrDefault(o =>
            string.Equals(o.OrderId, context.Message.OrderId, StringComparison.OrdinalIgnoreCase));

        if (order is null)
        {
            return context.RespondAsync(new OrderMutationExecuted(
                context.Message.CorrelationId, false, $"Order {context.Message.OrderId} was not found.", "unknown", []));
        }

        order = order.ApplyOverlay(overlay);
        var priorStatus = order.Status.ToString();
        var lineItems = order.LineItems
            .Select(li => new OrderLineItem(li.Sku, li.ProductName, li.Quantity, li.UnitPrice))
            .ToArray();

        if (!IsEligible(order.Status, context.Message.ActionType, out var failureReason))
        {
            return context.RespondAsync(new OrderMutationExecuted(
                context.Message.CorrelationId, false, failureReason, priorStatus, lineItems));
        }

        if (context.Message.ActionType == OrderActionType.Refund)
        {
            // Amount is plumbed all the way from the tool call through the saga to this consumer --
            // it must actually be validated and applied here, not just quoted back to the caller
            // (code review finding: a $50 refund on a $500 order was previously executed identically
            // to a full refund, since only Status was ever written).
            var amount = context.Message.Amount;
            if (amount is null or <= 0 || amount > order.TotalAmount)
            {
                return context.RespondAsync(new OrderMutationExecuted(
                    context.Message.CorrelationId,
                    false,
                    $"Refund amount {amount?.ToString() ?? "(none)"} is not valid for an order totaling {order.TotalAmount}.",
                    priorStatus,
                    lineItems));
            }

            overlay.Set(order.OrderId, OrderStatus.Refunded, amount);
        }
        else
        {
            overlay.Set(order.OrderId, OrderStatus.Cancelled);
        }

        return context.RespondAsync(new OrderMutationExecuted(
            context.Message.CorrelationId, true, null, priorStatus, lineItems));
    }

    private static bool IsEligible(OrderStatus current, OrderActionType actionType, out string? reason)
    {
        switch (actionType)
        {
            case OrderActionType.Refund when current == OrderStatus.Refunded:
                reason = "Order is already refunded.";
                return false;
            case OrderActionType.Refund when current == OrderStatus.Cancelled:
                reason = "Cannot refund a cancelled order.";
                return false;
            case OrderActionType.Cancellation when current == OrderStatus.Cancelled:
                reason = "Order is already cancelled.";
                return false;
            case OrderActionType.Cancellation when current == OrderStatus.Refunded:
                reason = "Cannot cancel a refunded order.";
                return false;
            case OrderActionType.Cancellation when current == OrderStatus.Delivered:
                reason = "Cannot cancel a delivered order.";
                return false;
            default:
                reason = null;
                return true;
        }
    }
}
