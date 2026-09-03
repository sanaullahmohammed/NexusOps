using NexusOps.Evaluation;

namespace NexusOps.Tests.Evaluation;

/// <summary>
/// Covers 007 FR-020/FR-021's real intent: an unrecognized or malformed flag must be a loud
/// usage error, never a silent fall-through to live mode — which would report itself as
/// "skipped" and exit 0, turning CI's `--validate-only` step into a no-op the moment the flag is
/// mistyped or renamed.
/// </summary>
public class CliOptionsTests
{
    [Fact]
    public void ValidateOnlyFlag_IsRecognized()
    {
        var options = CliOptions.Parse(["--validate-only"]);

        Assert.True(options.ValidateOnly);
    }

    [Fact]
    public void NoFlags_DefaultsToLiveMode()
    {
        var options = CliOptions.Parse([]);

        Assert.False(options.ValidateOnly);
    }

    [Fact]
    public void DatasetFlag_OverridesTheDefaultPath()
    {
        var options = CliOptions.Parse(["--dataset", "/tmp/custom.json"]);

        Assert.Equal("/tmp/custom.json", options.DatasetPath);
    }

    [Fact]
    public void BaseUrlFlag_OverridesTheDefault()
    {
        var options = CliOptions.Parse(["--base-url", "http://example.invalid"]);

        Assert.Equal("http://example.invalid", options.BaseUrl);
    }

    [Theory]
    [InlineData("--validte-only")]   // the exact typo this class exists to catch
    [InlineData("--Validate-Only")]
    [InlineData("--help")]
    public void AnUnrecognizedFlag_ThrowsRatherThanSilentlyFallingThroughToLiveMode(string typo)
    {
        var ex = Assert.Throws<CliUsageException>(() => CliOptions.Parse([typo]));

        Assert.Contains(typo, ex.Message);
    }

    [Fact]
    public void DatasetFlagAsTheFinalArgument_ThrowsRatherThanSilentlyKeepingTheDefault()
    {
        var ex = Assert.Throws<CliUsageException>(() => CliOptions.Parse(["--dataset"]));

        Assert.Contains("--dataset", ex.Message);
    }

    [Fact]
    public void BaseUrlFlagAsTheFinalArgument_ThrowsRatherThanSilentlyKeepingTheDefault()
    {
        var ex = Assert.Throws<CliUsageException>(() => CliOptions.Parse(["--base-url"]));

        Assert.Contains("--base-url", ex.Message);
    }
}
