namespace NexusOps.OrderService.Models;

/// <summary>Where an order sits in its lifecycle.</summary>
public enum OrderStatus
{
    Pending,
    Processing,
    Shipped,
    Delivered,
    Delayed,
    Cancelled
}

/// <summary>
/// Why an order is considered anomalous. Deliberately orthogonal to <see cref="OrderStatus"/>:
/// status describes lifecycle position, this describes what is wrong. An order with no reason
/// is not anomalous, whatever its status.
/// </summary>
public enum AnomalyReason
{
    Delayed,
    Missing,
    PaymentFailed
}

public sealed class LineItem
{
    public required string Sku { get; init; }
    public required string ProductName { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
}

public sealed class Order
{
    public required string OrderId { get; init; }
    public required string CustomerId { get; init; }
    public OrderStatus Status { get; init; }
    public decimal TotalAmount { get; init; }
    public DateOnly ExpectedDelivery { get; init; }
    public DateOnly? ActualDelivery { get; init; }
    public required List<LineItem> LineItems { get; init; }
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Why this order is anomalous, or <c>null</c> if it is not. The anomaly endpoint selects on
    /// this field; it never derives a classification from the request that asked for it.
    /// </summary>
    public AnomalyReason? AnomalyReason { get; init; }
}
