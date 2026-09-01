namespace NexusOps.Contracts.Dtos;

/// <summary>
/// The outcome of asking one of the three sources (order, inventory, product) for its portion of
/// a root-cause investigation.
/// </summary>
public enum SourceFindingStatus
{
    Pending,
    Succeeded,
    NotFound,
    Unavailable,
    TimedOut
}

/// <summary>
/// Overall completeness of a root-cause investigation, computed from the three source findings.
/// </summary>
public enum InvestigationCompleteness
{
    Complete,
    Degraded,
    Failed
}

/// <summary>
/// The consolidated result of a root-cause investigation for one order, returned by
/// <c>investigate_order_root_cause</c>.
/// </summary>
public sealed record RootCauseInvestigationResult(
    string OrderId,
    SourceFindingStatus OrderFinding,
    OrderSummary? Order,
    SourceFindingStatus InventoryFinding,
    InventoryLevel[] InventoryLevels,
    SourceFindingStatus ProductFinding,
    ProductDetail[] ProductDetails,
    InvestigationCompleteness Completeness,
    string[] DegradedSources);
