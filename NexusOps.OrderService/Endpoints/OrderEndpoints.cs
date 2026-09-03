using NexusOps.Contracts.Dtos;
using NexusOps.OrderService.Anomalies;
using NexusOps.OrderService.Data;

namespace NexusOps.OrderService.Endpoints;

public static class OrderEndpoints
{
    public static WebApplication MapOrderEndpoints(this WebApplication app)
    {
        app.MapGet("/orders/anomalies", (string? status, TimeProvider timeProvider, OrderMutationOverlay overlay) =>
        {
            Models.AnomalyReason? filter = null;

            if (!string.IsNullOrWhiteSpace(status))
            {
                filter = AnomalySelector.ParseReason(status);

                if (filter is null)
                {
                    return Results.BadRequest(
                        $"Unknown anomaly status '{status}'. Valid values are: {string.Join(", ", AnomalySelector.ValidFilters)}.");
                }
            }

            // Resolved once and threaded through both the seed and the projection, so every
            // date-derived value in the response agrees.
            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

            // AnomalySelector filters on AnomalyReason, not Status, and must keep doing so unmodified
            // (a seed order can be born Cancelled *because of* its anomaly, e.g. ORD-0009's
            // payment-failure cancellation -- that must still appear). So an order actioned via an
            // approval-gated refund/cancellation (feature 006) is excluded here, at the endpoint,
            // rather than by teaching AnomalySelector about Status: only orders the *overlay itself*
            // touched are excluded, never an order whose seed data already has a terminal status
            // (code review finding: the overlay was applied but had no actual effect on this endpoint,
            // since neither AnomalySelector nor OrderAnomaly ever looked at Status).
            var orders = OrderStore.GetOrders(today)
                .Where(o => !(overlay.TryGet(o.OrderId, out var ov) && ov.Status is Models.OrderStatus.Cancelled or Models.OrderStatus.Refunded));
            return Results.Ok(AnomalySelector.Select(orders, filter, today));
        });

        app.MapGet("/orders/{orderId}", (string orderId, TimeProvider timeProvider, OrderMutationOverlay overlay) =>
        {
            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

            var order = OrderStore.GetOrders(today).FirstOrDefault(o =>
                string.Equals(o.OrderId, orderId, StringComparison.OrdinalIgnoreCase));

            if (order is null)
            {
                return Results.NotFound($"Order {orderId} not found.");
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

            return Results.Ok(summary);
        });

        return app;
    }
}
