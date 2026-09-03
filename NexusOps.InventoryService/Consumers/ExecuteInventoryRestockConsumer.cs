using MassTransit;
using NexusOps.Contracts.Messages;
using NexusOps.InventoryService.Data;

namespace NexusOps.InventoryService.Consumers;

/// <summary>
/// Restocks the inventory reserved by a cancelled order's line items — cancellation's second
/// dependency, and the one whose failure this feature's compensation story is built around
/// (spec.md User Story 4). An unrecognized SKU still records a delta (no lookup against
/// <see cref="InventoryStore"/> happens here) — harmless in practice, since
/// <c>InventoryMutationOverlayExtensions.ApplyOverlay</c> only ever reads a delta for a SKU that
/// already exists in <see cref="InventoryStore.Records"/>, but this consumer does not itself
/// validate or skip unrecognized SKUs the way this doc comment previously (incorrectly) claimed.
/// </summary>
public sealed class ExecuteInventoryRestockConsumer(InventoryMutationOverlay overlay) : IConsumer<ExecuteInventoryRestock>
{
    public Task Consume(ConsumeContext<ExecuteInventoryRestock> context)
    {
        // A redelivered ExecuteInventoryRestock (e.g. OrderActionExecutionConsumer crashed after
        // this call succeeded but before publishing OrderActionExecutionCompleted) must not
        // double-credit the same restock -- unlike the order mutation, there is no natural
        // "already restocked" guard here (code review finding).
        if (overlay.TryMarkProcessed(context.Message.CorrelationId))
        {
            foreach (var line in context.Message.Lines)
            {
                overlay.AddDelta(line.Sku, line.Quantity);
            }
        }

        return context.RespondAsync(new InventoryRestockExecuted(context.Message.CorrelationId, true, null));
    }
}
