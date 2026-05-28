using NexusOps.Contracts;
using NexusOps.InventoryService.Models;

namespace NexusOps.InventoryService.Data;

public static class InventoryStore
{
    public static readonly IReadOnlyList<InventoryRecord> Records = new[]
    {
        // Electronics
        new InventoryRecord { Sku = SeedDataConstants.SkuElec001, ProductName = "Wireless Headphones Pro", WarehouseId = "WH-EAST-01", QuantityOnHand = 0, ReorderThreshold = 10, LastUpdated = new DateTime(2026, 5, 20) },
        new InventoryRecord { Sku = SeedDataConstants.SkuElec002, ProductName = "Bluetooth Speaker Pro", WarehouseId = "WH-EAST-01", QuantityOnHand = 45, ReorderThreshold = 15, LastUpdated = new DateTime(2026, 5, 25) },
        new InventoryRecord { Sku = SeedDataConstants.SkuElec003, ProductName = "Smart Watch Series 3", WarehouseId = "WH-EAST-01", QuantityOnHand = 22, ReorderThreshold = 8, LastUpdated = new DateTime(2026, 5, 24) },
        new InventoryRecord { Sku = SeedDataConstants.SkuElec004, ProductName = "USB-C Hub 7-Port", WarehouseId = "WH-WEST-01", QuantityOnHand = 60, ReorderThreshold = 20, LastUpdated = new DateTime(2026, 5, 23) },
        new InventoryRecord { Sku = SeedDataConstants.SkuElec005, ProductName = "Noise Cancelling Earbuds", WarehouseId = "WH-WEST-01", QuantityOnHand = 18, ReorderThreshold = 10, LastUpdated = new DateTime(2026, 5, 22) },
        // Apparel
        new InventoryRecord { Sku = SeedDataConstants.SkuAprl001, ProductName = "Classic Polo Shirt", WarehouseId = "WH-CENTRAL-01", QuantityOnHand = 120, ReorderThreshold = 30, LastUpdated = new DateTime(2026, 5, 18) },
        new InventoryRecord { Sku = SeedDataConstants.SkuAprl002, ProductName = "Running Shorts", WarehouseId = "WH-CENTRAL-01", QuantityOnHand = 85, ReorderThreshold = 25, LastUpdated = new DateTime(2026, 5, 17) },
        new InventoryRecord { Sku = SeedDataConstants.SkuAprl003, ProductName = "Yoga Pants", WarehouseId = "WH-CENTRAL-01", QuantityOnHand = 5, ReorderThreshold = 10, LastUpdated = new DateTime(2026, 5, 15) },
        new InventoryRecord { Sku = SeedDataConstants.SkuAprl004, ProductName = "Winter Jacket", WarehouseId = "WH-NORTH-01", QuantityOnHand = 40, ReorderThreshold = 12, LastUpdated = new DateTime(2026, 5, 14) },
        new InventoryRecord { Sku = SeedDataConstants.SkuAprl005, ProductName = "Casual Sneakers", WarehouseId = "WH-NORTH-01", QuantityOnHand = 95, ReorderThreshold = 20, LastUpdated = new DateTime(2026, 5, 13) },
        // Home & Garden
        new InventoryRecord { Sku = SeedDataConstants.SkuHome001, ProductName = "Garden Hose 50ft", WarehouseId = "WH-SOUTH-01", QuantityOnHand = 33, ReorderThreshold = 10, LastUpdated = new DateTime(2026, 5, 20) },
        new InventoryRecord { Sku = SeedDataConstants.SkuHome002, ProductName = "Ceramic Plant Pot Set", WarehouseId = "WH-SOUTH-01", QuantityOnHand = 48, ReorderThreshold = 15, LastUpdated = new DateTime(2026, 5, 19) },
        new InventoryRecord { Sku = SeedDataConstants.SkuHome003, ProductName = "Solar Garden Lights 10-Pack", WarehouseId = "WH-SOUTH-01", QuantityOnHand = 27, ReorderThreshold = 8, LastUpdated = new DateTime(2026, 5, 18) },
        new InventoryRecord { Sku = SeedDataConstants.SkuHome004, ProductName = "Compost Bin 80L", WarehouseId = "WH-EAST-01", QuantityOnHand = 15, ReorderThreshold = 5, LastUpdated = new DateTime(2026, 5, 17) },
        new InventoryRecord { Sku = SeedDataConstants.SkuHome005, ProductName = "Raised Garden Bed Kit", WarehouseId = "WH-EAST-01", QuantityOnHand = 12, ReorderThreshold = 4, LastUpdated = new DateTime(2026, 5, 16) }
    };
}
