using MassTransit;
using NexusOps.Contracts.Dtos;

namespace NexusOps.WorkflowOrchestrator.OrderAction;

/// <summary>
/// Durable record of one refund or cancellation request, from creation through its terminal
/// outcome. Order-action-specific — never referenced outside the <c>OrderAction</c> namespace.
/// </summary>
public sealed class OrderActionSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }

    public string CurrentState { get; set; } = default!;

    public OrderActionType ActionType { get; set; }

    public string OrderId { get; set; } = default!;

    /// <summary>Refund amount, defaulted to the order's total once validation confirms the order. <c>null</c> for cancellation.</summary>
    public decimal? Amount { get; set; }

    public string? Reason { get; set; }

    /// <summary>Captured from <c>RequestOrderRefund</c>/<c>RequestOrderCancellation</c>'s headers; cleared once responded.</summary>
    public Uri? RequestResponseAddress { get; set; }
    public Guid? RequestRequestId { get; set; }

    /// <summary>Captured from <c>ApproveOrderAction</c>'s headers at the moment of approval; cleared once responded.</summary>
    public Uri? ApprovalResponseAddress { get; set; }
    public Guid? ApprovalRequestId { get; set; }

    /// <summary>The order's status immediately before execution began — required input to a compensating reversal.</summary>
    public string? PriorStatus { get; set; }

    public OrderActionExecutionOutcome? ExecutionOutcome { get; set; }

    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>EF Core optimistic concurrency token; Npgsql maps a <c>uint</c> row-version property onto the <c>xmin</c> system column.</summary>
    public uint RowVersion { get; set; }
}
