namespace NexusOps.Contracts.Messages;

/// <summary>
/// Request/response between <c>OrderActionExecutionConsumer</c> and
/// <c>NexusOps.OrderService</c>'s <c>CompensateOrderMutationConsumer</c>. Issued only when a
/// cancellation's order mutation succeeded but its subsequent inventory restock failed (spec.md
/// FR-011, User Story 4). <see cref="RevertToStatus"/> is the <c>PriorStatus</c> captured by the
/// earlier <see cref="OrderMutationExecuted"/>.
/// </summary>
public sealed record CompensateOrderMutation(Guid CorrelationId, string OrderId, string RevertToStatus);

public sealed record OrderMutationCompensated(Guid CorrelationId, bool Success);
