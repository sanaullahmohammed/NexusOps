namespace NexusOps.Contracts.Messages;

/// <summary>One line item's restock quantity, for <see cref="ExecuteInventoryRestock"/>.</summary>
public sealed record InventoryRestockLine(string Sku, int Quantity);

/// <summary>
/// Request/response between <c>OrderActionExecutionConsumer</c> and
/// <c>NexusOps.InventoryService</c>'s <c>ExecuteInventoryRestockConsumer</c>. Cancellation only —
/// a refund has no inventory leg (research.md Decision 5).
/// </summary>
public sealed record ExecuteInventoryRestock(Guid CorrelationId, string OrderId, InventoryRestockLine[] Lines);

public sealed record InventoryRestockExecuted(Guid CorrelationId, bool Success, string? FailureReason);
