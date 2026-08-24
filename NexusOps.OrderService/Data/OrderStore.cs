using NexusOps.Contracts;
using NexusOps.OrderService.Models;

namespace NexusOps.OrderService.Data;

/// <summary>
/// In-memory seed data for the sample E-Commerce domain.
/// </summary>
/// <remarks>
/// <para>
/// Delivery dates are expressed as offsets from a supplied date rather than as literals. The
/// previous absolute dates were fixed in May–June 2026, so derived values such as
/// <c>daysOverdue</c> grew by one every day the repository existed — ORD-0001 was reporting 106
/// days overdue by August.
/// </para>
/// <para>
/// "Today" is a parameter, not state. An earlier revision resolved it in the constructor from an
/// injected <see cref="TimeProvider"/>, but the store was a DI singleton, so the seed froze at
/// process start while the endpoint recomputed the current date per request. The two drifted apart
/// on a long-lived host: ORD-0002 is seeded three days overdue as the deliberate medium-severity
/// example, and would start reporting high after roughly five days of uptime. Taking the date as an
/// argument means the caller resolves it once and every derived value agrees by construction.
/// </para>
/// </remarks>
public static class OrderStore
{
    /// <summary>Returns the seeded orders positioned relative to <paramref name="today"/>.</summary>
    public static IReadOnlyList<Order> GetOrders(DateOnly today) =>
    [
        // ORD-0001 — delayed well past the escalation threshold (high severity)
        new Order
        {
            OrderId = SeedDataConstants.Ord0001,
            CustomerId = "CUST-001",
            Status = OrderStatus.Delayed,
            AnomalyReason = AnomalyReason.Delayed,
            TotalAmount = 249.99m,
            ExpectedDelivery = today.AddDays(-14),
            ActualDelivery = null,
            CreatedAt = today.AddDays(-23).ToDateTime(TimeOnly.MinValue),
            LineItems =
            [
                new LineItem { Sku = SeedDataConstants.SkuElec002, ProductName = "Bluetooth Speaker Pro", Quantity = 1, UnitPrice = 249.99m }
            ]
        },
        // ORD-0002 — delayed but inside the escalation threshold (medium severity)
        new Order
        {
            OrderId = SeedDataConstants.Ord0002,
            CustomerId = "CUST-002",
            Status = OrderStatus.Delayed,
            AnomalyReason = AnomalyReason.Delayed,
            TotalAmount = 89.98m,
            ExpectedDelivery = today.AddDays(-3),
            ActualDelivery = null,
            CreatedAt = today.AddDays(-12).ToDateTime(TimeOnly.MinValue),
            LineItems =
            [
                new LineItem { Sku = SeedDataConstants.SkuAprl001, ProductName = "Classic Polo Shirt", Quantity = 2, UnitPrice = 44.99m }
            ]
        },
        // ORD-0003 — processing; references SKU-ELEC-001 (zero stock) — cross-service integrity
        new Order
        {
            OrderId = SeedDataConstants.Ord0003,
            CustomerId = "CUST-003",
            Status = OrderStatus.Processing,
            TotalAmount = 299.99m,
            ExpectedDelivery = today.AddDays(8),
            ActualDelivery = null,
            CreatedAt = today.AddDays(-4).ToDateTime(TimeOnly.MinValue),
            LineItems =
            [
                new LineItem { Sku = SeedDataConstants.SkuElec001, ProductName = "Wireless Headphones Pro", Quantity = 1, UnitPrice = 299.99m }
            ]
        },
        // ORD-0004 — shipped
        new Order
        {
            OrderId = SeedDataConstants.Ord0004,
            CustomerId = "CUST-004",
            Status = OrderStatus.Shipped,
            TotalAmount = 134.97m,
            ExpectedDelivery = today.AddDays(6),
            ActualDelivery = null,
            CreatedAt = today.AddDays(-2).ToDateTime(TimeOnly.MinValue),
            LineItems =
            [
                new LineItem { Sku = SeedDataConstants.SkuHome001, ProductName = "Garden Hose 50ft", Quantity = 3, UnitPrice = 44.99m }
            ]
        },
        // ORD-0005 — shipped
        new Order
        {
            OrderId = SeedDataConstants.Ord0005,
            CustomerId = "CUST-005",
            Status = OrderStatus.Shipped,
            TotalAmount = 59.99m,
            ExpectedDelivery = today.AddDays(5),
            ActualDelivery = null,
            CreatedAt = today.AddDays(-3).ToDateTime(TimeOnly.MinValue),
            LineItems =
            [
                new LineItem { Sku = SeedDataConstants.SkuAprl002, ProductName = "Running Shorts", Quantity = 1, UnitPrice = 59.99m }
            ]
        },
        // ORD-0006 — delivered early
        new Order
        {
            OrderId = SeedDataConstants.Ord0006,
            CustomerId = "CUST-006",
            Status = OrderStatus.Delivered,
            TotalAmount = 199.99m,
            ExpectedDelivery = today.AddDays(-9),
            ActualDelivery = today.AddDays(-10),
            CreatedAt = today.AddDays(-14).ToDateTime(TimeOnly.MinValue),
            LineItems =
            [
                new LineItem { Sku = SeedDataConstants.SkuElec003, ProductName = "Smart Watch Series 3", Quantity = 1, UnitPrice = 199.99m }
            ]
        },
        // ORD-0007 — delivered early
        new Order
        {
            OrderId = SeedDataConstants.Ord0007,
            CustomerId = "CUST-007",
            Status = OrderStatus.Delivered,
            TotalAmount = 79.98m,
            ExpectedDelivery = today.AddDays(-6),
            ActualDelivery = today.AddDays(-7),
            CreatedAt = today.AddDays(-12).ToDateTime(TimeOnly.MinValue),
            LineItems =
            [
                new LineItem { Sku = SeedDataConstants.SkuHome002, ProductName = "Ceramic Plant Pot Set", Quantity = 2, UnitPrice = 39.99m }
            ]
        },
        // ORD-0008 — processing
        new Order
        {
            OrderId = SeedDataConstants.Ord0008,
            CustomerId = "CUST-008",
            Status = OrderStatus.Processing,
            TotalAmount = 149.99m,
            ExpectedDelivery = today.AddDays(12),
            ActualDelivery = null,
            CreatedAt = today.AddDays(-1).ToDateTime(TimeOnly.MinValue),
            LineItems =
            [
                new LineItem { Sku = SeedDataConstants.SkuElec004, ProductName = "USB-C Hub 7-Port", Quantity = 1, UnitPrice = 149.99m }
            ]
        },
        // ORD-0009 — cancelled after the payment could not be captured
        new Order
        {
            OrderId = SeedDataConstants.Ord0009,
            CustomerId = "CUST-009",
            Status = OrderStatus.Cancelled,
            AnomalyReason = AnomalyReason.PaymentFailed,
            TotalAmount = 44.99m,
            ExpectedDelivery = today.AddDays(-1),
            ActualDelivery = null,
            CreatedAt = today.AddDays(-8).ToDateTime(TimeOnly.MinValue),
            LineItems =
            [
                new LineItem { Sku = SeedDataConstants.SkuAprl003, ProductName = "Yoga Pants", Quantity = 1, UnitPrice = 44.99m }
            ]
        },
        // ORD-0010 — pending
        new Order
        {
            OrderId = SeedDataConstants.Ord0010,
            CustomerId = "CUST-010",
            Status = OrderStatus.Pending,
            TotalAmount = 89.99m,
            ExpectedDelivery = today.AddDays(17),
            ActualDelivery = null,
            CreatedAt = today.ToDateTime(TimeOnly.MinValue),
            LineItems =
            [
                new LineItem { Sku = SeedDataConstants.SkuHome003, ProductName = "Solar Garden Lights 10-Pack", Quantity = 1, UnitPrice = 89.99m }
            ]
        },
        // ORD-0011 — shipped, expected delivery long past, never arrived and never scanned
        new Order
        {
            OrderId = SeedDataConstants.Ord0011,
            CustomerId = "CUST-011",
            Status = OrderStatus.Shipped,
            AnomalyReason = AnomalyReason.Missing,
            TotalAmount = 129.98m,
            ExpectedDelivery = today.AddDays(-21),
            ActualDelivery = null,
            CreatedAt = today.AddDays(-30).ToDateTime(TimeOnly.MinValue),
            LineItems =
            [
                new LineItem { Sku = SeedDataConstants.SkuElec005, ProductName = "Wireless Charging Pad", Quantity = 2, UnitPrice = 64.99m }
            ]
        }
    ];
}
