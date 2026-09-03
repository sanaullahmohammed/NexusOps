using System.Collections.Concurrent;
using NexusOps.InventoryService.Models;

namespace NexusOps.InventoryService.Data;

/// <summary>
/// Process-lifetime-durable cumulative delta layered on top of <see cref="InventoryStore"/>'s
/// static, immutable seed records — the minimal-touch way to give a cancellation's inventory
/// restock a real, observable effect (research.md Decision 7). Positive only in this feature (a
/// restock always adds stock back); a negative delta is not produced by any consumer this feature
/// adds.
/// </summary>
public sealed class InventoryMutationOverlay
{
    private readonly ConcurrentDictionary<string, int> _deltas = new(StringComparer.OrdinalIgnoreCase);

    // Unlike ExecuteOrderMutationConsumer's mutation (naturally guarded: a redelivered attempt finds
    // the order already in its target status and fails eligibility rather than reapplying), a
    // restock has no such natural guard -- a redelivered ExecuteInventoryRestock would silently
    // double-credit the same SKUs. TryAdd is atomic: only the first delivery for a given
    // CorrelationId proceeds (code review finding).
    private readonly ConcurrentDictionary<Guid, byte> _processedCorrelationIds = new();

    public int GetDelta(string sku) => _deltas.TryGetValue(sku, out var delta) ? delta : 0;

    public void AddDelta(string sku, int quantity) => _deltas.AddOrUpdate(sku, quantity, (_, existing) => existing + quantity);

    /// <summary>Returns <c>true</c> the first time this <paramref name="correlationId"/> is seen; <c>false</c> on any redelivery.</summary>
    public bool TryMarkProcessed(Guid correlationId) => _processedCorrelationIds.TryAdd(correlationId, 0);
}

public static class InventoryMutationOverlayExtensions
{
    /// <summary>Returns <paramref name="record"/> with <see cref="InventoryRecord.QuantityOnHand"/> adjusted by any recorded delta.</summary>
    public static InventoryRecord ApplyOverlay(this InventoryRecord record, InventoryMutationOverlay overlay)
    {
        var delta = overlay.GetDelta(record.Sku);
        if (delta == 0)
        {
            return record;
        }

        return new InventoryRecord
        {
            Sku = record.Sku,
            ProductName = record.ProductName,
            WarehouseId = record.WarehouseId,
            QuantityOnHand = record.QuantityOnHand + delta,
            ReorderThreshold = record.ReorderThreshold,
            LastUpdated = record.LastUpdated
        };
    }
}
