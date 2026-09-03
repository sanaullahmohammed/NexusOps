namespace NexusOps.Contracts.Dtos;

public sealed record OrderSummary(
    string OrderId,
    string CustomerId,
    string Status,
    decimal TotalAmount,
    DateOnly ExpectedDelivery,
    DateOnly? ActualDelivery,
    OrderLineItem[] LineItems,
    decimal? RefundedAmount = null);

public sealed record OrderLineItem(
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice);
