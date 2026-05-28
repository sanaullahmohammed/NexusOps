using NexusOps.Contracts;
using NexusOps.OrderService.Models;

namespace NexusOps.OrderService.Data;

public static class OrderStore
{
    public static readonly IReadOnlyList<Order> Orders = new[]
    {
        // ORD-0001 — delayed
        new Order
        {
            OrderId = SeedDataConstants.Ord0001,
            CustomerId = "CUST-001",
            Status = OrderStatus.Delayed,
            TotalAmount = 249.99m,
            ExpectedDelivery = new DateOnly(2026, 5, 10),
            ActualDelivery = null,
            CreatedAt = new DateTime(2026, 5, 1),
            LineItems =
            [
                new LineItem { Sku = SeedDataConstants.SkuElec002, ProductName = "Bluetooth Speaker Pro", Quantity = 1, UnitPrice = 249.99m }
            ]
        },
        // ORD-0002 — delayed
        new Order
        {
            OrderId = SeedDataConstants.Ord0002,
            CustomerId = "CUST-002",
            Status = OrderStatus.Delayed,
            TotalAmount = 89.98m,
            ExpectedDelivery = new DateOnly(2026, 5, 12),
            ActualDelivery = null,
            CreatedAt = new DateTime(2026, 5, 3),
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
            ExpectedDelivery = new DateOnly(2026, 6, 1),
            ActualDelivery = null,
            CreatedAt = new DateTime(2026, 5, 20),
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
            ExpectedDelivery = new DateOnly(2026, 5, 30),
            ActualDelivery = null,
            CreatedAt = new DateTime(2026, 5, 22),
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
            ExpectedDelivery = new DateOnly(2026, 5, 29),
            ActualDelivery = null,
            CreatedAt = new DateTime(2026, 5, 21),
            LineItems =
            [
                new LineItem { Sku = SeedDataConstants.SkuAprl002, ProductName = "Running Shorts", Quantity = 1, UnitPrice = 59.99m }
            ]
        },
        // ORD-0006 — delivered
        new Order
        {
            OrderId = SeedDataConstants.Ord0006,
            CustomerId = "CUST-006",
            Status = OrderStatus.Delivered,
            TotalAmount = 199.99m,
            ExpectedDelivery = new DateOnly(2026, 5, 15),
            ActualDelivery = new DateOnly(2026, 5, 14),
            CreatedAt = new DateTime(2026, 5, 10),
            LineItems =
            [
                new LineItem { Sku = SeedDataConstants.SkuElec003, ProductName = "Smart Watch Series 3", Quantity = 1, UnitPrice = 199.99m }
            ]
        },
        // ORD-0007 — delivered
        new Order
        {
            OrderId = SeedDataConstants.Ord0007,
            CustomerId = "CUST-007",
            Status = OrderStatus.Delivered,
            TotalAmount = 79.98m,
            ExpectedDelivery = new DateOnly(2026, 5, 18),
            ActualDelivery = new DateOnly(2026, 5, 17),
            CreatedAt = new DateTime(2026, 5, 12),
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
            ExpectedDelivery = new DateOnly(2026, 6, 5),
            ActualDelivery = null,
            CreatedAt = new DateTime(2026, 5, 26),
            LineItems =
            [
                new LineItem { Sku = SeedDataConstants.SkuElec004, ProductName = "USB-C Hub 7-Port", Quantity = 1, UnitPrice = 149.99m }
            ]
        },
        // ORD-0009 — cancelled
        new Order
        {
            OrderId = SeedDataConstants.Ord0009,
            CustomerId = "CUST-009",
            Status = OrderStatus.Cancelled,
            TotalAmount = 44.99m,
            ExpectedDelivery = new DateOnly(2026, 5, 25),
            ActualDelivery = null,
            CreatedAt = new DateTime(2026, 5, 18),
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
            ExpectedDelivery = new DateOnly(2026, 6, 10),
            ActualDelivery = null,
            CreatedAt = new DateTime(2026, 5, 27),
            LineItems =
            [
                new LineItem { Sku = SeedDataConstants.SkuHome003, ProductName = "Solar Garden Lights 10-Pack", Quantity = 1, UnitPrice = 89.99m }
            ]
        }
    };
}
