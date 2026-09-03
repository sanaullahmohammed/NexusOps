using NexusOps.Contracts.Dtos;

namespace NexusOps.Contracts.Messages;

/// <summary>Published by <c>OrderActionExecutionConsumer</c> back to <c>OrderActionSaga</c> once execution finishes.</summary>
/// <param name="PriorStatus">
/// The order's status immediately before execution began, as observed by <c>ExecuteOrderMutation</c>.
/// Persisted onto the saga so it is visible on the durable record, not just passed through the
/// execution consumer's own local call chain (code review finding — the column existed and was
/// migrated but nothing ever wrote it).
/// </param>
public sealed record OrderActionExecutionCompleted(Guid CorrelationId, OrderActionExecutionOutcome Outcome, string Detail, string? PriorStatus);
