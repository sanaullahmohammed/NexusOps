using MassTransit;
using NexusOps.Contracts.Dtos;

namespace NexusOps.WorkflowOrchestrator.OrderInvestigation;

/// <summary>
/// Durable record of one order root-cause investigation. One row per investigation
/// (<see cref="CorrelationId"/> is the primary key). Order-specific — never referenced outside
/// the <c>OrderInvestigation</c> namespace.
/// </summary>
public sealed class OrderInvestigationSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }

    public string CurrentState { get; set; } = default!;

    public string OrderId { get; set; } = default!;

    /// <summary>Captured from the originating request's headers; cleared once the response has been sent.</summary>
    public Uri? ResponseAddress { get; set; }

    public Guid? RequestId { get; set; }

    public SourceFindingStatus OrderFinding { get; set; } = SourceFindingStatus.Pending;
    public SourceFindingStatus InventoryFinding { get; set; } = SourceFindingStatus.Pending;
    public SourceFindingStatus ProductFinding { get; set; } = SourceFindingStatus.Pending;

    public string? OrderResultJson { get; set; }
    public string? InventoryResultJson { get; set; }
    public string? ProductResultJson { get; set; }

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>EF Core optimistic concurrency token; the Npgsql provider maps a row-version
    /// property of this shape onto the <c>xmin</c> system column automatically.</summary>
    public uint RowVersion { get; set; }

    public bool AllSourcesReported =>
        OrderFinding != SourceFindingStatus.Pending
        && InventoryFinding != SourceFindingStatus.Pending
        && ProductFinding != SourceFindingStatus.Pending;
}
