using NexusOps.Contracts.Dtos;

namespace NexusOps.Contracts.Messages;

/// <summary>Published by <c>OrderActionSaga</c> on request; consumed by <c>OrderActionValidationConsumer</c>.</summary>
public sealed record BeginActionValidation(Guid CorrelationId, string OrderId);

/// <summary>
/// Published by <c>OrderActionValidationConsumer</c> back to the saga, after mapping the response
/// of a reused <c>RequestOrderFinding</c>/<c>OrderFindingReported</c> call (feature 005's existing
/// contract). Deliberately not the raw <see cref="OrderFindingReported"/> event itself — that type
/// is also consumed by feature 005's <c>OrderInvestigationSaga</c>, and re-publishing it here would
/// broadcast this feature's validation traffic onto a queue 005 owns (research.md Decision 1).
/// </summary>
public sealed record ActionValidationCompleted(Guid CorrelationId, SourceFindingStatus Status, OrderSummary? Order);
