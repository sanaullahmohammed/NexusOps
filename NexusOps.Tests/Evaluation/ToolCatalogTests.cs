using NexusOps.Evaluation;

namespace NexusOps.Tests.Evaluation;

/// <summary>
/// Guards the exact drift DatasetValidator's own path-consistency check depends on:
/// <see cref="ToolCatalog.KnownTools"/> is reflected live from <c>ToolNames</c>, but
/// <see cref="ToolCatalog.ToolPaths"/> is a separate, hand-maintained table. A tool added to
/// <c>ToolNames</c> without a matching <c>ToolPaths</c> entry must fail loudly here — as a clear
/// assertion, not as a <c>KeyNotFoundException</c> the first time some other code indexes it.
/// </summary>
public class ToolCatalogTests
{
    [Fact]
    public void EveryKnownTool_HasAToolPathsEntry()
    {
        var missing = ToolCatalog.KnownTools.Where(t => !ToolCatalog.ToolPaths.ContainsKey(t)).ToList();

        Assert.True(missing.Count == 0,
            $"ToolCatalog.ToolPaths is missing an entry for: {string.Join(", ", missing)}. " +
            "Add an entry when adding a new tool, or DatasetValidator/tests using ToolPaths will break.");
    }

    [Fact]
    public void EveryToolPathsEntry_IsAKnownTool()
    {
        var unknown = ToolCatalog.ToolPaths.Keys.Where(t => !ToolCatalog.KnownTools.Contains(t)).ToList();

        Assert.True(unknown.Count == 0,
            $"ToolCatalog.ToolPaths has stale entries for tools ToolNames no longer defines: {string.Join(", ", unknown)}.");
    }

    [Fact]
    public void EveryToolPathsValue_IsASupportedPath()
    {
        Assert.All(ToolCatalog.ToolPaths.Values, path => Assert.True(path is ToolCatalog.DirectPath or ToolCatalog.SagaPath));
    }
}
