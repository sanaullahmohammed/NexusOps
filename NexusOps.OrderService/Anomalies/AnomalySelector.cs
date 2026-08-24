using NexusOps.Contracts.Dtos;
using NexusOps.OrderService.Models;

namespace NexusOps.OrderService.Anomalies;

/// <summary>
/// Selects and projects anomalous orders. Kept separate from the endpoint so the classification
/// rules can be tested directly rather than through an HTTP host.
/// </summary>
public static class AnomalySelector
{
    /// <summary>Days past expected delivery beyond which a delayed order escalates to high severity.</summary>
    public const int DelayEscalationDays = 7;

    /// <summary>The filter values the endpoint accepts, in the order they are documented.</summary>
    public static readonly string[] ValidFilters = ["delayed", "missing", "payment-failed"];

    /// <summary>Maps the wire vocabulary onto the domain enum. Returns <c>null</c> for an unrecognised value.</summary>
    public static AnomalyReason? ParseReason(string status) => status.Trim().ToLowerInvariant() switch
    {
        "delayed" => AnomalyReason.Delayed,
        "missing" => AnomalyReason.Missing,
        "payment-failed" => AnomalyReason.PaymentFailed,
        _ => null
    };

    public static string ToWireValue(AnomalyReason reason) => reason switch
    {
        AnomalyReason.Delayed => "delayed",
        AnomalyReason.Missing => "missing",
        AnomalyReason.PaymentFailed => "payment-failed",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unmapped anomaly reason.")
    };

    /// <summary>
    /// Returns the anomalous orders, optionally narrowed to one reason. An order is anomalous
    /// because of what it is, so the filter selects — it never changes how an order is classified.
    /// </summary>
    public static OrderAnomaly[] Select(IEnumerable<Order> orders, AnomalyReason? filter, DateOnly today) =>
        orders
            .Where(o => o.AnomalyReason is not null)
            .Where(o => filter is null || o.AnomalyReason == filter)
            .Select(o => ToAnomaly(o, today))
            .ToArray();

    public static OrderAnomaly ToAnomaly(Order order, DateOnly today)
    {
        var reason = order.AnomalyReason
            ?? throw new ArgumentException($"Order {order.OrderId} is not anomalous.", nameof(order));

        // Only a delayed order has a meaningful overdue count; the others are not late, they are wrong.
        int? daysOverdue = reason == AnomalyReason.Delayed
            ? Math.Max(0, today.DayNumber - order.ExpectedDelivery.DayNumber)
            : null;

        return new OrderAnomaly(
            OrderId: order.OrderId,
            AnomalyType: ToWireValue(reason),
            Severity: DeriveSeverity(reason, daysOverdue),
            DaysOverdue: daysOverdue,
            CustomerId: order.CustomerId,
            TotalAmount: order.TotalAmount,
            ExpectedDelivery: order.ExpectedDelivery,
            LineItems: order.LineItems
                .Select(li => new OrderLineItem(li.Sku, li.ProductName, li.Quantity, li.UnitPrice))
                .ToArray());
    }

    /// <summary>
    /// A missing order and a failed payment are always high — one is a lost customer, the other is
    /// lost revenue. A delay escalates with how long it has been outstanding.
    /// </summary>
    public static string DeriveSeverity(AnomalyReason reason, int? daysOverdue) => reason switch
    {
        AnomalyReason.Missing => "high",
        AnomalyReason.PaymentFailed => "high",
        AnomalyReason.Delayed => daysOverdue > DelayEscalationDays ? "high" : "medium",
        _ => "medium"
    };
}
