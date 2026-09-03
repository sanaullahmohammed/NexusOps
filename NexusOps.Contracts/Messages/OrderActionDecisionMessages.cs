using NexusOps.Contracts.Dtos;

namespace NexusOps.Contracts.Messages;

/// <summary>
/// Published by <c>POST /api/approvals/{id}/approve</c>. Blocks until the saga's execution
/// consumer reports a final outcome — the response carries the real result, not an interim
/// acknowledgment (research.md Decision 3).
/// </summary>
public sealed record ApproveOrderAction(Guid ApprovalReference);

/// <summary>Published by <c>POST /api/approvals/{id}/reject</c>. Responds immediately.</summary>
public sealed record RejectOrderAction(Guid ApprovalReference, string? Reason);

/// <summary>
/// The response to both <see cref="ApproveOrderAction"/> and <see cref="RejectOrderAction"/>.
/// <see cref="ExecutionOutcome"/> is set only when <see cref="DecisionStatus"/> is
/// <see cref="OrderActionDecisionOutcome.Approved"/>.
/// </summary>
public sealed record OrderActionDecisionResult(
    Guid ApprovalReference,
    OrderActionDecisionOutcome DecisionStatus,
    OrderActionExecutionOutcome? ExecutionOutcome,
    string Message);
