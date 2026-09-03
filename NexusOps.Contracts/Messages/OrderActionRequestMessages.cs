namespace NexusOps.Contracts.Messages;

/// <summary>
/// Published by AgentHost's <c>request_order_refund</c> tool handler as a MassTransit request; the
/// response is <see cref="Dtos.OrderActionRequestResult"/>. Never executes anything by itself — it
/// only ever produces a pending, approval-gated reference (spec.md FR-003).
/// </summary>
public sealed record RequestOrderRefund(string OrderId, decimal? Amount, string? Reason);

/// <summary>
/// Published by AgentHost's <c>request_order_cancellation</c> tool handler as a MassTransit
/// request; the response is <see cref="Dtos.OrderActionRequestResult"/>.
/// </summary>
public sealed record RequestOrderCancellation(string OrderId, string? Reason);
