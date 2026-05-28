namespace NexusOps.InventoryService.Models;

public sealed class InventoryRecord
{
    public required string Sku { get; init; }
    public required string ProductName { get; init; }
    public required string WarehouseId { get; init; }
    public int QuantityOnHand { get; init; }
    public int ReorderThreshold { get; init; }
    public DateTime LastUpdated { get; init; }
}
