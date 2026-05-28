using NexusOps.Contracts.Dtos;
using NexusOps.OrderService.Data;
using NexusOps.OrderService.Models;

namespace NexusOps.OrderService.Endpoints;

public static class OrderEndpoints
{
    public static WebApplication MapOrderEndpoints(this WebApplication app)
    {
        app.MapGet("/orders/anomalies", (string? status) =>
        {
            var anomalyStatuses = new HashSet<OrderStatus> { OrderStatus.Delayed, OrderStatus.Cancelled };

            if (!string.IsNullOrWhiteSpace(status))
            {
                var mapped = status.ToLowerInvariant() switch
                {
                    "delayed" => OrderStatus.Delayed,
                    "missing" => OrderStatus.Cancelled,  // map missing → cancelled for seed purposes
                    "payment-failed" => OrderStatus.Cancelled,
                    _ => (OrderStatus?)null
                };
                if (mapped is null)
                {
                    return Results.BadRequest($"Unknown anomaly status: {status}");
                }
                anomalyStatuses = [mapped.Value];
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var anomalies = OrderStore.Orders
                .Where(o => anomalyStatuses.Contains(o.Status))
                .Select(o => new OrderAnomaly(
                    OrderId: o.OrderId,
                    AnomalyType: o.Status switch
                    {
                        OrderStatus.Delayed => "delayed",
                        OrderStatus.Cancelled => status?.ToLowerInvariant() == "payment-failed" ? "payment-failed" : "missing",
                        _ => "delayed"
                    },
                    Severity: o.Status == OrderStatus.Delayed ? "high" : "medium",
                    DaysOverdue: o.Status == OrderStatus.Delayed
                        ? (int?)(today.DayNumber - o.ExpectedDelivery.DayNumber)
                        : null))
                .ToArray();

            return Results.Ok(anomalies);
        });

        app.MapGet("/orders/{orderId}", (string orderId) =>
        {
            var order = OrderStore.Orders.FirstOrDefault(o =>
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
                LineItems: order.LineItems.Select(li => new OrderLineItem(
                    Sku: li.Sku,
                    ProductName: li.ProductName,
                    Quantity: li.Quantity,
                    UnitPrice: li.UnitPrice)).ToArray());

            return Results.Ok(summary);
        });

        return app;
    }
}
