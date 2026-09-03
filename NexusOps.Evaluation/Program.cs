using NexusOps.Evaluation;

CliOptions options;
try
{
    options = CliOptions.Parse(args);
}
catch (CliUsageException ex)
{
    Console.Error.WriteLine($"Usage error: {ex.Message}");
    return 2;
}

if (options.ValidateOnly)
{
    return RunValidateOnly(options.DatasetPath);
}

return await RunLiveAsync(options);

static int RunValidateOnly(string datasetPath)
{
    IReadOnlyList<EvaluationCase?> cases;
    try
    {
        cases = EvaluationDataset.Load(datasetPath);
    }
    catch (DatasetLoadException ex)
    {
        ConsoleReport.WriteValidationFailures([new ValidationIssue(null, ex.Message)]);
        return 1;
    }

    var issues = DatasetValidator.Validate(cases);
    if (issues.Count > 0)
    {
        ConsoleReport.WriteValidationFailures(issues);
        return 1;
    }

    ConsoleReport.WriteValidationSuccess(cases.Count);
    return 0;
}

static async Task<int> RunLiveAsync(CliOptions options)
{
    using var httpClient = new HttpClient { BaseAddress = new Uri(options.BaseUrl) };
    var runner = new LiveRunner(httpClient);

    // FR-016: reachability is determined before any dataset prompt is sent — the dataset isn't
    // even loaded yet on this path.
    if (!await runner.ProbeReachabilityAsync())
    {
        ConsoleReport.WriteSkipped(options.BaseUrl);
        return 0;
    }

    IReadOnlyList<EvaluationCase?> cases;
    try
    {
        cases = EvaluationDataset.Load(options.DatasetPath);
    }
    catch (DatasetLoadException ex)
    {
        ConsoleReport.WriteValidationFailures([new ValidationIssue(null, ex.Message)]);
        return 1;
    }

    var results = new List<EvaluationResult>(cases.Count);
    for (var i = 0; i < cases.Count; i++)
    {
        var evaluationCase = cases[i];

        // A bare JSON `null` array element deserializes cleanly (not a load failure) — report it
        // as a failed case by position rather than dereferencing it.
        if (evaluationCase is null)
        {
            const string message = "Case is null (a JSON 'null' entry is not a valid case).";
            results.Add(new EvaluationResult($"(index {i})", ExpectedTool: "(unknown)", ToolsInvoked: [], Passed: false, Error: message));
            ConsoleReport.WriteMalformedCase(i, message);
            continue;
        }

        var result = await runner.RunCaseAsync(evaluationCase);
        results.Add(result);
        ConsoleReport.WriteCaseResult(evaluationCase, result);
    }

    var passed = results.Count(r => r.Passed);
    var summary = new EvaluationSummary(
        Total: results.Count,
        Passed: passed,
        Failed: results.Count - passed,
        PassRate: results.Count == 0 ? 0d : (double)passed / results.Count);

    ConsoleReport.WriteSummary(summary);
    return summary.Failed == 0 ? 0 : 1;
}
