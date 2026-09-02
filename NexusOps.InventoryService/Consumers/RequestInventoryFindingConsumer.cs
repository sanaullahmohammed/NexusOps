using MassTransit;
using NexusOps.Contracts.Dtos;
using NexusOps.Contracts.Messages;
using NexusOps.InventoryService.Data;

namespace NexusOps.InventoryService.Consumers;

/// <summary>
/// Answers the fan-out coordinator's batch stock lookup for a root-cause investigation. A SKU
/// with no inventory record doesn't fail the whole source — it's reported in <c>SkusNotFound</c>
/// (spec.md Edge Cases).
/// </summary>
public sealed class RequestInventoryFindingConsumer : IConsumer<RequestInventoryFinding>
{
    public Task Consume(ConsumeContext<RequestInventoryFinding> context)
    {
        var levels = new List<InventoryLevel>();
        var notFound = new List<string>();

        foreach (var sku in context.Message.Skus)
        {
            var record = InventoryStore.Records.FirstOrDefault(r =>
                string.Equals(r.Sku, sku, StringComparison.OrdinalIgnoreCase));

            if (record is null)
            {
                notFound.Add(sku);
                continue;
            }

            levels.Add(new InventoryLevel(
                Sku: record.Sku,
                ProductName: record.ProductName,
                WarehouseId: record.WarehouseId,
                QuantityOnHand: record.QuantityOnHand,
                ReorderThreshold: record.ReorderThreshold,
                LastUpdated: record.LastUpdated));
        }

        var status = levels.Count > 0 ? SourceFindingStatus.Succeeded : SourceFindingStatus.NotFound;

        return context.RespondAsync(new InventoryFindingReported(
            context.Message.CorrelationId, status, levels.ToArray(), notFound.ToArray()));
    }
}
