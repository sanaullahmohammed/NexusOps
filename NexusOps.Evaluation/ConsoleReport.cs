namespace NexusOps.Evaluation;

/// <summary>All console output for both modes, kept in one place so the two report shapes
/// (validation defects vs. live pass/fail) stay visually consistent.</summary>
public static class ConsoleReport
{
    public static void WriteValidationSuccess(int caseCount) =>
        Console.WriteLine($"Dataset valid: {caseCount} case(s) validated. No network access or credentials were used.");

    public static void WriteValidationFailures(IReadOnlyList<ValidationIssue> issues)
    {
        Console.WriteLine($"Dataset validation FAILED: {issues.Count} issue(s) found.");
        foreach (var issue in issues)
        {
            var label = issue.CaseId is null ? "[dataset]" : $"[{issue.CaseId}]";
            Console.WriteLine($"  {label} {issue.Message}");
        }
    }

    public static void WriteSkipped(string baseUrl)
    {
        Console.WriteLine("SKIPPED: live evaluation was not run.");
        Console.WriteLine($"Could not reach an AgentHost at {baseUrl} (GET /health did not succeed within 3s).");
        Console.WriteLine();
        Console.WriteLine("Live evaluation requires a running AgentHost with Azure AI credentials configured. To run it locally:");
        Console.WriteLine("  1. dotnet user-secrets set \"AzureAI:ApiKey\" \"<your-api-key>\" --project NexusOps.AgentHost");
        Console.WriteLine("  2. dotnet run --project NexusOps.AppHost   (or: dotnet run --project NexusOps.AgentHost)");
        Console.WriteLine("  3. dotnet run --project NexusOps.Evaluation -- --base-url <agent-host-url>");
        Console.WriteLine();
        Console.WriteLine("This is not a failure — no dataset prompts were sent.");
    }

    /// <summary>A dataset case that could not even be read (e.g. a null JSON array element) —
    /// reported as a failure by position, since there is no prompt or case id to show.</summary>
    public static void WriteMalformedCase(int index, string message) =>
        Console.WriteLine($"[FAIL] (index {index}): {message}");

    public static void WriteCaseResult(EvaluationCase evaluationCase, EvaluationResult result)
    {
        var status = result.Passed ? "PASS" : "FAIL";
        var actual = result.Error is not null
            ? $"error: {result.Error}"
            : result.ToolsInvoked.Count == 0
                ? "no tool invoked"
                : string.Join(", ", result.ToolsInvoked);

        Console.WriteLine($"[{status}] {result.CaseId}: expected '{result.ExpectedTool}', got {actual} — \"{evaluationCase.Prompt}\"");
    }

    public static void WriteSummary(EvaluationSummary summary)
    {
        Console.WriteLine();
        Console.WriteLine("Summary");
        Console.WriteLine("-------");
        Console.WriteLine($"Total:     {summary.Total}");
        Console.WriteLine($"Passed:    {summary.Passed}");
        Console.WriteLine($"Failed:    {summary.Failed}");
        Console.WriteLine($"Pass rate: {summary.PassRate:P0}");
    }
}
