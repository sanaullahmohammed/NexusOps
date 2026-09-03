using MassTransit;

namespace NexusOps.Contracts.Messages;

/// <summary>
/// Published by <c>OrderActionSaga</c> once per terminal outcome (executed, rejected, failed,
/// failed-and-compensated); consumed by <c>notification-service</c>, a non-.NET amqplib consumer.
/// The exchange name is pinned via <see cref="EntityNameAttribute"/> rather than left to
/// MassTransit's default CLR-type-derived naming, so the Node.js consumer has one fixed,
/// documented name to bind against (research.md Decision 9).
/// </summary>
/// <param name="Outcome">
/// A plain string, not <see cref="Dtos.OrderActionExecutionOutcome"/> — this message crosses into
/// a non-.NET consumer, so its JSON shape must not depend on a .NET enum's serialization
/// convention. One of "Executed", "Rejected", "Failed", "FailedAndCompensated" (data-model.md).
/// </param>
[EntityName("notification-requested")]
public sealed record NotificationRequested(Guid CorrelationId, string OrderId, string ActionType, string Outcome, string Message);
