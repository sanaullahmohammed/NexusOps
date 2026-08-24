using NexusOps.Contracts.Dtos;
using NexusOps.OrderService.Anomalies;
using NexusOps.OrderService.Data;

namespace NexusOps.OrderService.Endpoints;

public static class OrderEndpoints
{
    public static WebApplication MapOrderEndpoints(this WebApplication app)
    {
        app.MapGet("/orders/anomalies", (string? status, OrderStore store, TimeProvider timeProvider) =>
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

            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

            return Results.Ok(AnomalySelector.Select(store.Orders, filter, today));
        });

        app.MapGet("/orders/{orderId}", (string orderId, OrderStore store) =>
        {
            var order = store.Orders.FirstOrDefault(o =>
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
