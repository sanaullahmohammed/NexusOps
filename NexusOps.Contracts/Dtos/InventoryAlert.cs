namespace NexusOps.Contracts.Dtos;

public sealed record InventoryAlert(
    string Sku,
    string ProductName,
    string WarehouseId,
    int QuantityOnHand,
    int ReorderThreshold,
    bool IsOutOfStock);

public sealed record InventoryLevel(
    string Sku,
    string ProductName,
    string WarehouseId,
    int QuantityOnHand,
    int ReorderThreshold,
    DateTime LastUpdated);
