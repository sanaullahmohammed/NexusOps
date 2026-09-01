namespace NexusOps.Contracts.Messages;

/// <summary>
/// Published by AgentHost's <c>investigate_order_root_cause</c> tool handler as a MassTransit
/// request; the response is <see cref="Dtos.RootCauseInvestigationResult"/>.
/// </summary>
public sealed record InvestigateOrderRootCause(string OrderId);
