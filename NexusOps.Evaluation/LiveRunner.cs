using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace NexusOps.Evaluation;

/// <summary>The outcome of evaluating one <see cref="EvaluationCase"/> against a live AgentHost.</summary>
public sealed record EvaluationResult(string CaseId, string ExpectedTool, IReadOnlyList<string> ToolsInvoked, bool Passed, string? Error);

/// <summary>The aggregate outcome of a full live-evaluation run.</summary>
public sealed record EvaluationSummary(int Total, int Passed, int Failed, double PassRate);

/// <summary>Sends dataset prompts to a running AgentHost and scores the tool it invoked for each.</summary>
public sealed class LiveRunner(HttpClient httpClient)
{
    private static readonly TimeSpan ReachabilityTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CaseTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Determines whether a live agent is reachable, without sending any dataset prompt (FR-016).
    /// Any failure to reach it — connection refused, non-success status, timeout — is reported
    /// identically as "not reachable"; this method never throws.
    /// </summary>
    public async Task<bool> ProbeReachabilityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(ReachabilityTimeout);
            using var response = await httpClient.GetAsync("/health", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            // Deliberately unfiltered, matching this method's own "never throws" contract.
            return false;
        }
    }

    /// <summary>
    /// Sends one case's prompt as a fresh, session-less turn and scores whether the expected tool
    /// was among those the agent invoked. A request failure or timeout is recorded as a failed
    /// result rather than thrown, so the caller can continue through the remaining cases (FR-018).
    /// </summary>
    public async Task<EvaluationResult> RunCaseAsync(EvaluationCase evaluationCase, CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(CaseTimeout);

            using var response = await httpClient.PostAsJsonAsync("/api/chat", new ChatRequestPayload(evaluationCase.Prompt!), cts.Token);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<ChatResponsePayload>(cts.Token);
            var toolsInvoked = body?.ToolsInvoked ?? [];
            var passed = toolsInvoked.Contains(evaluationCase.ExpectedTool, StringComparer.Ordinal);

            return new EvaluationResult(evaluationCase.Id!, evaluationCase.ExpectedTool!, toolsInvoked, passed, Error: null);
        }
        catch (Exception ex)
        {
            // Deliberately unfiltered: a malformed response body (JsonException), an unexpected
            // content type (NotSupportedException), a dropped connection (HttpRequestException),
            // or a timeout must all fail this one case, never the whole run — that is FR-018's
            // entire point, and a narrower filter here previously let some of these escape and
            // abort every remaining case.
            return new EvaluationResult(evaluationCase.Id!, evaluationCase.ExpectedTool!, [], Passed: false, Error: ex.Message);
        }
    }

    private sealed record ChatRequestPayload([property: JsonPropertyName("prompt")] string Prompt);

    private sealed record ChatResponsePayload(
        [property: JsonPropertyName("response")] string? Response,
        [property: JsonPropertyName("sessionId")] string? SessionId,
        [property: JsonPropertyName("toolsInvoked")] IReadOnlyList<string>? ToolsInvoked);
}
