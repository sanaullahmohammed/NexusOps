using NexusOps.Contracts.Dtos;

namespace NexusOps.Contracts.Messages;

/// <summary>Request/response pair between the fan-out coordinator and <c>NexusOps.OrderService</c>.</summary>
public sealed record RequestOrderFinding(Guid CorrelationId, string OrderId);

public sealed record OrderFindingReported(
    Guid CorrelationId,
    SourceFindingStatus Status,
    OrderSummary? Order);
