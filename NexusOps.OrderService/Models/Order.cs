namespace NexusOps.OrderService.Models;

public enum OrderStatus
{
    Pending,
    Processing,
    Shipped,
    Delivered,
    Delayed,
    Cancelled
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
}
