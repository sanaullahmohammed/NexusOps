namespace NexusOps.Contracts.Dtos;

/// <summary>Which curated mutation this action represents.</summary>
public enum OrderActionType
{
    Refund,
    Cancellation
}

/// <summary>The status of a just-created action request, before any approval decision.</summary>
public enum OrderActionStatus
{
    AwaitingApproval,

    /// <summary>The order was confirmed not to exist — a completed, trustworthy validation result.</summary>
    NotFound,

    /// <summary>
    /// The order service could not be reached or timed out during validation — distinct from
    /// <see cref="NotFound"/> because the question "does this order exist?" was never actually
    /// answered (mirrors feature 005's Unavailable/TimedOut vs. NotFound distinction in
    /// SourceFindingStatus). No pending reference was created; retrying the request is expected to
    /// succeed once the outage clears.
    /// </summary>
    Unavailable
}

/// <summary>The outcome of an approval/rejection decision against a specific reference.</summary>
public enum OrderActionDecisionOutcome
{
    Approved,
    Rejected,
    AlreadyDecided,
    NotFound
}

/// <summary>
/// The real result of executing an approved action. <c>FailedAndCompensated</c> is distinct from
/// <c>Failed</c>: the former means a change was made and then reversed; the latter means nothing
/// was ever changed.
/// </summary>
public enum OrderActionExecutionOutcome
{
    Executed,
    Failed,
    FailedAndCompensated
}

/// <summary>
/// Returned by <c>request_order_refund</c>/<c>request_order_cancellation</c> — never claims the
/// action has executed. <see cref="ApprovalReference"/> is meaningless to act on when
/// <see cref="Status"/> is <see cref="OrderActionStatus.NotFound"/>.
/// </summary>
public sealed record OrderActionRequestResult(
    string OrderId,
    OrderActionType ActionType,
    Guid ApprovalReference,
    OrderActionStatus Status,
    decimal? Amount);
