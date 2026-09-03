namespace NexusOps.Evaluation;

/// <summary>One defect found while validating the dataset. <see cref="CaseId"/> is null for a
/// dataset-wide issue (missing file, malformed JSON, a tool or path with zero covering cases).</summary>
public sealed record ValidationIssue(string? CaseId, string Message);

/// <summary>
/// Credential-free, offline dataset validation (FR-005 through FR-011). Collects every issue in a
/// single pass rather than stopping at the first, so a dataset author sees every defect at once.
/// </summary>
public static class DatasetValidator
{
    public const int MinCaseCount = 20;
    public const int MaxCaseCount = 30;

    public static IReadOnlyList<ValidationIssue> Validate(IReadOnlyList<EvaluationCase?> cases)
    {
        var issues = new List<ValidationIssue>();

        if (cases.Count is < MinCaseCount or > MaxCaseCount)
        {
            issues.Add(new ValidationIssue(null,
                $"Dataset has {cases.Count} case(s); expected between {MinCaseCount} and {MaxCaseCount}."));
        }

        // A bare JSON `null` array element deserializes cleanly to a null case (not a parse
        // failure) — report it as a defect by position, then validate the rest as usual rather
        // than crashing on the first null dereference.
        for (var i = 0; i < cases.Count; i++)
        {
            if (cases[i] is null)
            {
                issues.Add(new ValidationIssue(null, $"Case at index {i} is null (a JSON 'null' entry is not a valid case)."));
            }
        }

        var nonNullCases = cases.OfType<EvaluationCase>().ToList();

        ValidateRequiredFields(nonNullCases, issues);
        ValidateUniqueIds(nonNullCases, issues);
        ValidateToolsAndPaths(nonNullCases, issues);
        ValidateCoverage(nonNullCases, issues);

        return issues;
    }

    private static void ValidateRequiredFields(IReadOnlyList<EvaluationCase> cases, List<ValidationIssue> issues)
    {
        foreach (var c in cases)
        {
            var label = string.IsNullOrWhiteSpace(c.Id) ? "(missing id)" : c.Id;

            if (string.IsNullOrWhiteSpace(c.Id))
            {
                issues.Add(new ValidationIssue(label, "Case is missing a required 'id'."));
            }
            if (string.IsNullOrWhiteSpace(c.Prompt))
            {
                issues.Add(new ValidationIssue(label, "Case is missing a required 'prompt'."));
            }
            if (string.IsNullOrWhiteSpace(c.ExpectedTool))
            {
                issues.Add(new ValidationIssue(label, "Case is missing a required 'expectedTool'."));
            }
            if (string.IsNullOrWhiteSpace(c.ExpectedPath))
            {
                issues.Add(new ValidationIssue(label, "Case is missing a required 'expectedPath'."));
            }
        }
    }

    private static void ValidateUniqueIds(IReadOnlyList<EvaluationCase> cases, List<ValidationIssue> issues)
    {
        var duplicates = cases
            .Where(c => !string.IsNullOrWhiteSpace(c.Id))
            .GroupBy(c => c.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1);

        foreach (var group in duplicates)
        {
            issues.Add(new ValidationIssue(group.Key, $"Case id '{group.Key}' is used by {group.Count()} cases; case ids must be unique."));
        }
    }

    private static void ValidateToolsAndPaths(IReadOnlyList<EvaluationCase> cases, List<ValidationIssue> issues)
    {
        foreach (var c in cases)
        {
            if (string.IsNullOrWhiteSpace(c.ExpectedTool) || string.IsNullOrWhiteSpace(c.ExpectedPath))
            {
                continue; // already reported as a missing-field issue
            }

            if (!ToolCatalog.KnownTools.Contains(c.ExpectedTool))
            {
                issues.Add(new ValidationIssue(c.Id, $"'{c.ExpectedTool}' is not a recognized tool name."));
                continue;
            }

            if (c.ExpectedPath is not (ToolCatalog.DirectPath or ToolCatalog.SagaPath))
            {
                issues.Add(new ValidationIssue(c.Id, $"'{c.ExpectedPath}' is not a supported path; expected '{ToolCatalog.DirectPath}' or '{ToolCatalog.SagaPath}'."));
                continue;
            }

            // KnownTools is reflected from ToolNames; ToolPaths is a separate, hand-maintained
            // table (research.md Decision 4) — the two can fall out of sync if a tool is added
            // without a matching ToolPaths entry. That is a bug in this project, not the dataset,
            // but it must still be reported rather than crash the whole validation run.
            if (!ToolCatalog.ToolPaths.TryGetValue(c.ExpectedTool, out var actualPath))
            {
                issues.Add(new ValidationIssue(c.Id,
                    $"Internal inconsistency: '{c.ExpectedTool}' is a known tool but NexusOps.Evaluation's ToolCatalog.ToolPaths has no entry for it. This is a bug in NexusOps.Evaluation, not the dataset."));
                continue;
            }

            if (!string.Equals(actualPath, c.ExpectedPath, StringComparison.Ordinal))
            {
                issues.Add(new ValidationIssue(c.Id,
                    $"expectedPath '{c.ExpectedPath}' does not match tool '{c.ExpectedTool}''s actual path '{actualPath}'."));
            }
        }
    }

    private static void ValidateCoverage(IReadOnlyList<EvaluationCase> cases, List<ValidationIssue> issues)
    {
        var coveredTools = cases
            .Where(c => c.ExpectedTool is not null && ToolCatalog.KnownTools.Contains(c.ExpectedTool))
            .Select(c => c.ExpectedTool!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var tool in ToolCatalog.KnownTools.Except(coveredTools).Order(StringComparer.Ordinal))
        {
            issues.Add(new ValidationIssue(null, $"No dataset case covers tool '{tool}'."));
        }

        var coveredPaths = cases
            .Where(c => c.ExpectedPath is ToolCatalog.DirectPath or ToolCatalog.SagaPath)
            .Select(c => c.ExpectedPath!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var path in new[] { ToolCatalog.DirectPath, ToolCatalog.SagaPath }.Except(coveredPaths))
        {
            issues.Add(new ValidationIssue(null, $"No dataset case covers the '{path}' path."));
        }
    }
}
