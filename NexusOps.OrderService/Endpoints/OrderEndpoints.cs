using NexusOps.Contracts.Dtos;
using NexusOps.OrderService.Anomalies;
using NexusOps.OrderService.Data;

namespace NexusOps.OrderService.Endpoints;

public static class OrderEndpoints
{
    public static WebApplication MapOrderEndpoints(this WebApplication app)
    {
        app.MapGet("/orders/anomalies", (string? status, TimeProvider timeProvider) =>
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

            return Results.Ok(AnomalySelector.Select(OrderStore.GetOrders(today), filter, today));
        });

        app.MapGet("/orders/{orderId}", (string orderId, TimeProvider timeProvider) =>
        {
            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

            var order = OrderStore.GetOrders(today).FirstOrDefault(o =>
                string.Equals(o.OrderId, orderId, StringComparison.OrdinalIgnoreCase));

            if (order is null)
            {
                return Results.NotFound($"Order {orderId} not found.");
            }

            var summary = new OrderSummary(
                OrderId: order.OrderId,
                CustomerId: order.CustomerId,
                Status: order.Status.ToString().ToLowerInvariant(),
                TotalAmount: order.TotalAmount,
                ExpectedDelivery: order.ExpectedDelivery,
                ActualDelivery: order.ActualDelivery,
                LineItems: order.LineItems
                    .Select(li => new OrderLineItem(li.Sku, li.ProductName, li.Quantity, li.UnitPrice))
                    .ToArray());

            return Results.Ok(summary);
        });

        return app;
    }
}
