using NexusOps.Evaluation;

namespace NexusOps.Tests.Evaluation;

/// <summary>
/// Covers 007 FR-005 through FR-011: dataset validation is credential-free, catches every kind of
/// authoring mistake, and reports every defect it finds in a single pass.
/// </summary>
public class DatasetValidatorTests
{
    private const string DirectTool = "get_order_details";      // Direct, per ToolCatalog
    private const string SagaTool = "request_order_refund";     // Saga, per ToolCatalog

    private static EvaluationCase Case(string id, string tool = DirectTool, string path = "Direct", string? prompt = "a realistic prompt") =>
        new(id, prompt, tool, path);

    /// <summary>A dataset covering every known tool exactly twice, evenly split Direct/Saga-ish, sized within [20, 30].</summary>
    private static List<EvaluationCase> ValidDataset()
    {
        var cases = new List<EvaluationCase>();
        var i = 0;
        foreach (var tool in ToolCatalog.KnownTools.OrderBy(t => t, StringComparer.Ordinal))
        {
            // A missing entry here is itself the exact drift DatasetValidator's own defensive
            // TryGetValue guards against — fail with a clear message, not a KeyNotFoundException
            // that errors the whole test class.
            Assert.True(ToolCatalog.ToolPaths.TryGetValue(tool, out var path),
                $"ToolCatalog.ToolPaths is missing an entry for known tool '{tool}'.");
            cases.Add(Case($"case-{i++:000}", tool, path));
            cases.Add(Case($"case-{i++:000}", tool, path));
        }
        return cases; // 9 tools * 2 = 18 -> pad to 20
    }

    private static List<EvaluationCase> ValidDatasetPadded()
    {
        var cases = ValidDataset();
        cases.Add(Case("case-pad-1"));
        cases.Add(Case("case-pad-2"));
        return cases; // 20
    }

    [Fact]
    public void AValidDataset_ProducesNoIssues()
    {
        var issues = DatasetValidator.Validate(ValidDatasetPadded());

        Assert.Empty(issues);
    }

    [Fact]
    public void ADuplicateCaseId_IsReported()
    {
        var cases = ValidDatasetPadded();
        cases[1] = cases[1] with { Id = cases[0].Id };

        var issues = DatasetValidator.Validate(cases);

        Assert.Contains(issues, i => i.CaseId == cases[0].Id && i.Message.Contains("unique"));
    }

    [Fact]
    public void AnUnrecognizedTool_IsReported()
    {
        var cases = ValidDatasetPadded();
        cases[0] = cases[0] with { ExpectedTool = "get_weather_forecast" };

        var issues = DatasetValidator.Validate(cases);

        Assert.Contains(issues, i => i.CaseId == cases[0].Id && i.Message.Contains("get_weather_forecast"));
    }

    [Fact]
    public void AnUnsupportedPathValue_IsReported()
    {
        var cases = ValidDatasetPadded();
        cases[0] = cases[0] with { ExpectedPath = "Async" };

        var issues = DatasetValidator.Validate(cases);

        Assert.Contains(issues, i => i.CaseId == cases[0].Id && i.Message.Contains("Async"));
    }

    [Fact]
    public void APathInconsistentWithItsTool_IsReported()
    {
        var cases = ValidDatasetPadded();
        cases[0] = cases[0] with { ExpectedTool = DirectTool, ExpectedPath = "Saga" };

        var issues = DatasetValidator.Validate(cases);

        Assert.Contains(issues, i => i.CaseId == cases[0].Id && i.Message.Contains("does not match"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AMissingRequiredField_IsReported(string? blank)
    {
        var cases = ValidDatasetPadded();
        cases[0] = cases[0] with { Prompt = blank };

        var issues = DatasetValidator.Validate(cases);

        Assert.Contains(issues, i => i.Message.Contains("prompt"));
    }

    [Fact]
    public void TooFewCases_IsReported()
    {
        var cases = ValidDataset().Take(5).ToList();

        var issues = DatasetValidator.Validate(cases);

        Assert.Contains(issues, i => i.CaseId is null && i.Message.Contains("case(s)"));
    }

    [Fact]
    public void AToolWithNoCoveringCase_IsReported()
    {
        var cases = ValidDatasetPadded().Where(c => c.ExpectedTool != SagaTool).ToList();
        // Pad back to a valid count so only the coverage issue fires.
        while (cases.Count < DatasetValidator.MinCaseCount)
        {
            cases.Add(Case($"case-extra-{cases.Count}"));
        }

        var issues = DatasetValidator.Validate(cases);

        Assert.Contains(issues, i => i.CaseId is null && i.Message.Contains(SagaTool));
    }

    [Fact]
    public void ANullDatasetElement_IsReportedNotThrown()
    {
        var cases = new List<EvaluationCase?>(ValidDatasetPadded());
        cases[3] = null;

        var issues = DatasetValidator.Validate(cases);

        Assert.Contains(issues, i => i.CaseId is null && i.Message.Contains("index 3") && i.Message.Contains("null"));
    }

    [Fact]
    public void ANullDatasetElement_DoesNotSuppressValidationOfTheOtherCases()
    {
        var cases = new List<EvaluationCase?>(ValidDatasetPadded());
        cases[3] = null;
        cases[0] = cases[0]! with { ExpectedTool = "not_a_real_tool" };

        var issues = DatasetValidator.Validate(cases);

        Assert.Contains(issues, i => i.CaseId == cases[0]!.Id);
    }

    [Fact]
    public void EveryDefectInADataset_IsReportedInASinglePass()
    {
        var cases = ValidDatasetPadded();
        cases[0] = cases[0] with { ExpectedTool = "not_a_real_tool" };
        cases[1] = cases[1] with { Id = cases[2].Id };

        var issues = DatasetValidator.Validate(cases);

        Assert.True(issues.Count >= 2, "Expected both defects to be reported in one pass, not just the first.");
    }
}
