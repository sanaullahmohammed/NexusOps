using System.Reflection;
using NexusOps.Contracts;

namespace NexusOps.Evaluation;

/// <summary>Known tool names and paths, sourced from the project's own curated tool list.</summary>
public static class ToolCatalog
{
    public const string DirectPath = "Direct";
    public const string SagaPath = "Saga";

    /// <summary>
    /// Every tool name this project curates, reflected directly from <see cref="ToolNames"/> so this
    /// set can never drift from the tools AgentHost actually wires up (research.md Decision 4).
    /// </summary>
    public static readonly IReadOnlySet<string> KnownTools = typeof(ToolNames)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(string) && !f.Name.EndsWith("Description", StringComparison.Ordinal))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Which path (Direct or Saga) each known tool belongs to. ToolNames itself carries no path
    /// metadata, so this is asserted directly — reviewed like a test fixture, and exactly the kind
    /// of drift this feature's own dataset validation is designed to catch if it ever falls out of
    /// sync with a newly added tool.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ToolPaths = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ToolNames.InvestigateOrderAnomaly] = DirectPath,
        [ToolNames.GetOrderDetails] = DirectPath,
        [ToolNames.GetInventoryAlerts] = DirectPath,
        [ToolNames.GetInventoryLevel] = DirectPath,
        [ToolNames.GetProductDetails] = DirectPath,
        [ToolNames.ListProductsByCategory] = DirectPath,
        [ToolNames.InvestigateOrderRootCause] = SagaPath,
        [ToolNames.RequestOrderRefund] = SagaPath,
        [ToolNames.RequestOrderCancellation] = SagaPath,
    };
}
