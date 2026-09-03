using NexusOps.Contracts.Dtos;

namespace NexusOps.Contracts.Messages;

/// <summary>Published by <c>OrderActionSaga</c> on approval; consumed by <c>OrderActionExecutionConsumer</c>.</summary>
public sealed record BeginOrderActionExecution(Guid CorrelationId, OrderActionType ActionType, string OrderId, decimal? Amount);
