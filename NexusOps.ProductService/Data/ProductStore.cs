using NexusOps.Contracts;
using NexusOps.ProductService.Models;

namespace NexusOps.ProductService.Data;

public static class ProductStore
{
    public static readonly IReadOnlyList<Product> Products = new[]
    {
        // Electronics (5)
        new Product { ProductId = SeedDataConstants.Prd0001, Sku = SeedDataConstants.SkuElec001, Name = "Wireless Headphones Pro", Description = "Premium over-ear headphones with 40-hour battery and active noise cancellation.", Category = "Electronics", UnitPrice = 299.99m, WeightKg = 0.28m },
        new Product { ProductId = SeedDataConstants.Prd0002, Sku = SeedDataConstants.SkuElec002, Name = "Bluetooth Speaker Pro", Description = "360° surround sound portable speaker, waterproof IPX7, 20-hour playtime.", Category = "Electronics", UnitPrice = 249.99m, WeightKg = 0.62m },
        new Product { ProductId = SeedDataConstants.Prd0003, Sku = SeedDataConstants.SkuElec003, Name = "Smart Watch Series 3", Description = "Health & fitness tracker with GPS, heart rate, SpO2, and 7-day battery.", Category = "Electronics", UnitPrice = 199.99m, WeightKg = 0.05m },
        new Product { ProductId = SeedDataConstants.Prd0004, Sku = SeedDataConstants.SkuElec004, Name = "USB-C Hub 7-Port", Description = "Aluminum 7-in-1 USB-C hub with 4K HDMI, 100W PD, SD card reader, 3× USB-A.", Category = "Electronics", UnitPrice = 149.99m, WeightKg = 0.09m },
        new Product { ProductId = SeedDataConstants.Prd0005, Sku = SeedDataConstants.SkuElec005, Name = "Noise Cancelling Earbuds", Description = "True wireless earbuds with ANC, 30-hour total battery, IPX4 splash-resistant.", Category = "Electronics", UnitPrice = 179.99m, WeightKg = 0.06m },
        // Apparel (5)
        new Product { ProductId = SeedDataConstants.Prd0006, Sku = SeedDataConstants.SkuAprl001, Name = "Classic Polo Shirt", Description = "100% pique cotton polo available in 8 colours, machine washable.", Category = "Apparel", UnitPrice = 44.99m, WeightKg = 0.22m },
        new Product { ProductId = SeedDataConstants.Prd0007, Sku = SeedDataConstants.SkuAprl002, Name = "Running Shorts", Description = "Lightweight 2-in-1 running shorts with built-in liner and deep pockets.", Category = "Apparel", UnitPrice = 59.99m, WeightKg = 0.18m },
        new Product { ProductId = SeedDataConstants.Prd0008, Sku = SeedDataConstants.SkuAprl003, Name = "Yoga Pants", Description = "High-waist 4-way stretch yoga pants with moisture-wicking fabric.", Category = "Apparel", UnitPrice = 44.99m, WeightKg = 0.24m },
        new Product { ProductId = SeedDataConstants.Prd0009, Sku = SeedDataConstants.SkuAprl004, Name = "Winter Jacket", Description = "Insulated parka with detachable hood, water-resistant shell, -10 °C rated.", Category = "Apparel", UnitPrice = 189.99m, WeightKg = 1.10m },
        new Product { ProductId = SeedDataConstants.Prd0010, Sku = SeedDataConstants.SkuAprl005, Name = "Casual Sneakers", Description = "Canvas low-top sneakers with memory foam insole, available in 6 colours.", Category = "Apparel", UnitPrice = 79.99m, WeightKg = 0.55m },
        // Home & Garden (5)
        new Product { ProductId = SeedDataConstants.Prd0011, Sku = SeedDataConstants.SkuHome001, Name = "Garden Hose 50ft", Description = "Expandable no-kink garden hose with 8-pattern spray nozzle, 50 ft.", Category = "Home & Garden", UnitPrice = 44.99m, WeightKg = 0.95m },
        new Product { ProductId = SeedDataConstants.Prd0012, Sku = SeedDataConstants.SkuHome002, Name = "Ceramic Plant Pot Set", Description = "Set of 3 handcrafted ceramic plant pots with drainage holes and bamboo saucers.", Category = "Home & Garden", UnitPrice = 39.99m, WeightKg = 1.40m },
        new Product { ProductId = SeedDataConstants.Prd0013, Sku = SeedDataConstants.SkuHome003, Name = "Solar Garden Lights 10-Pack", Description = "Stainless steel solar-powered path lights, auto on/off, IP65 weatherproof.", Category = "Home & Garden", UnitPrice = 89.99m, WeightKg = 1.20m },
        new Product { ProductId = SeedDataConstants.Prd0014, Sku = SeedDataConstants.SkuHome004, Name = "Compost Bin 80L", Description = "Aerated 80-litre compost bin with locking lid and side access hatch.", Category = "Home & Garden", UnitPrice = 54.99m, WeightKg = 2.80m },
        new Product { ProductId = SeedDataConstants.Prd0015, Sku = SeedDataConstants.SkuHome005, Name = "Raised Garden Bed Kit", Description = "Cedar wood raised bed kit, 120 × 60 cm, tool-free assembly.", Category = "Home & Garden", UnitPrice = 119.99m, WeightKg = 4.50m }
    };
}
