using NexusOps.Contracts.Dtos;

namespace NexusOps.Contracts.Messages;

/// <summary>
/// Request/response between <c>OrderActionExecutionConsumer</c> and
/// <c>NexusOps.OrderService</c>'s <c>ExecuteOrderMutationConsumer</c>. One shared contract handles
/// both refund and cancellation — they differ only in target status (research.md Decision 8).
/// </summary>
public sealed record ExecuteOrderMutation(Guid CorrelationId, OrderActionType ActionType, string OrderId, decimal? Amount);

/// <summary>
/// <see cref="Success"/> is <c>false</c> when the order is not eligible for the requested action
/// (e.g., already refunded or already cancelled) — never thrown, never silently applied (spec.md
/// FR-013). <see cref="PriorStatus"/> and <see cref="LineItems"/> are populated even on failure,
/// since the caller needs <see cref="PriorStatus"/> regardless for a possible later compensation.
/// </summary>
public sealed record OrderMutationExecuted(
    Guid CorrelationId,
    bool Success,
    string? FailureReason,
    string PriorStatus,
    OrderLineItem[] LineItems);
