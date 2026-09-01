using NexusOps.Contracts.Dtos;

namespace NexusOps.Contracts.Messages;

/// <summary>Request/response pair between the fan-out coordinator and <c>NexusOps.InventoryService</c>.</summary>
public sealed record RequestInventoryFinding(Guid CorrelationId, string[] Skus);

public sealed record InventoryFindingReported(
    Guid CorrelationId,
    SourceFindingStatus Status,
    InventoryLevel[] Levels,
    string[] SkusNotFound);
