namespace NexusOps.Contracts.Messages;

/// <summary>
/// Published by <c>OrderInvestigationSaga</c> to kick off the fan-out coordinator. Internal to the
/// workflow orchestrator — never seen by AgentHost or the domain services.
/// </summary>
public sealed record BeginInvestigationFanOut(Guid CorrelationId, string OrderId);
