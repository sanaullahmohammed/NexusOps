namespace NexusOps.Evaluation;

/// <summary>Thrown for a malformed command line — an unrecognized flag or a flag missing its
/// required value. Distinct from a validation/live-run failure: this is a usage error.</summary>
public sealed class CliUsageException(string message) : Exception(message);

/// <summary>Hand-rolled argument parsing — three flags do not justify a parsing library
/// (research.md Decision 5).</summary>
public sealed record CliOptions(bool ValidateOnly, string DatasetPath, string BaseUrl)
{
    private static readonly string DefaultDatasetPath = Path.Combine(AppContext.BaseDirectory, "Data", "eval-cases.json");
    private const string DefaultBaseUrl = "http://localhost:5186";

    public const string SupportedFlags = "--validate-only, --dataset <path>, --base-url <url>";

    /// <summary>
    /// Parses <paramref name="args"/>, throwing <see cref="CliUsageException"/> for any
    /// unrecognized flag or a flag missing its required value — silently ignoring either would
    /// let a typo (e.g. "--validte-only") fall through to live mode, which then reports itself
    /// as skipped and exits 0, turning a CI validation step into a silent no-op.
    /// </summary>
    public static CliOptions Parse(string[] args)
    {
        var validateOnly = false;
        var datasetPath = DefaultDatasetPath;
        var baseUrl = Environment.GetEnvironmentVariable("AGENTHOST_BASE_URL") is { Length: > 0 } fromEnv
            ? fromEnv
            : DefaultBaseUrl;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--validate-only":
                    validateOnly = true;
                    break;
                case "--dataset":
                    datasetPath = RequireValue(args, ref i, "--dataset");
                    break;
                case "--base-url":
                    baseUrl = RequireValue(args, ref i, "--base-url");
                    break;
                default:
                    throw new CliUsageException($"Unrecognized argument: '{args[i]}'. Supported flags: {SupportedFlags}.");
            }
        }

        return new CliOptions(validateOnly, datasetPath, baseUrl);
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
        {
            throw new CliUsageException($"'{flag}' requires a value. Supported flags: {SupportedFlags}.");
        }

        return args[++i];
    }
}
