using NexusOps.Contracts.Dtos;
using NexusOps.InventoryService.Data;

namespace NexusOps.InventoryService.Endpoints;

public static class InventoryEndpoints
{
    public static WebApplication MapInventoryEndpoints(this WebApplication app)
    {
        app.MapGet("/inventory/alerts", (bool? outOfStockOnly, InventoryMutationOverlay overlay) =>
        {
            var alerts = InventoryStore.Records
                .Select(r => r.ApplyOverlay(overlay))
                .Where(r => outOfStockOnly == true
                    ? r.QuantityOnHand == 0
                    : r.QuantityOnHand <= r.ReorderThreshold)
                .Select(r => new InventoryAlert(
                    Sku: r.Sku,
                    ProductName: r.ProductName,
                    WarehouseId: r.WarehouseId,
                    QuantityOnHand: r.QuantityOnHand,
                    ReorderThreshold: r.ReorderThreshold,
                    IsOutOfStock: r.QuantityOnHand == 0))
                .ToArray();

            return Results.Ok(alerts);
        });

        app.MapGet("/inventory/{sku}", (string sku, InventoryMutationOverlay overlay) =>
        {
            var record = InventoryStore.Records.FirstOrDefault(r =>
                string.Equals(r.Sku, sku, StringComparison.OrdinalIgnoreCase));

            if (record is null)
            {
                return Results.NotFound($"Inventory record for SKU {sku} not found.");
            }

            record = record.ApplyOverlay(overlay);

            var level = new InventoryLevel(
                Sku: record.Sku,
                ProductName: record.ProductName,
                WarehouseId: record.WarehouseId,
                QuantityOnHand: record.QuantityOnHand,
                ReorderThreshold: record.ReorderThreshold,
                LastUpdated: record.LastUpdated);

            return Results.Ok(level);
        });

        return app;
    }
}
