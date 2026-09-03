using System.Net;
using System.Net.Http.Json;
using System.Text;
using NexusOps.Evaluation;

namespace NexusOps.Tests.Evaluation;

/// <summary>
/// Covers 007 FR-016 through FR-018: reachability is checked before any dataset prompt is sent, an
/// unreachable AgentHost never causes a second call, and a per-case failure is recorded rather than
/// thrown so the run can continue.
/// </summary>
public class LiveRunnerTests
{
    /// <summary>Answers each request from a queue of canned responses (or a thrown exception),
    /// recording every request path it saw.</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> RequestedPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedPaths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpClient Client(HttpMessageHandler handler) =>
        new(handler, disposeHandler: false) { BaseAddress = new Uri("http://stub-agent-host") };

    private static EvaluationCase Case(string tool = "get_order_details") =>
        new("case-001", "a prompt", tool, "Direct");

    [Fact]
    public async Task WhenHealthCheckFails_ReachabilityIsFalseAndNoOtherCallIsMade()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var runner = new LiveRunner(Client(handler));

        var reachable = await runner.ProbeReachabilityAsync();

        Assert.False(reachable);
        Assert.Equal(["/health"], handler.RequestedPaths);
    }

    [Fact]
    public async Task WhenTheConnectionIsRefused_ReachabilityIsFalseNotThrown()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        var runner = new LiveRunner(Client(handler));

        var reachable = await runner.ProbeReachabilityAsync();

        Assert.False(reachable);
    }

    [Fact]
    public async Task WhenHealthCheckSucceeds_ReachabilityIsTrue()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var runner = new LiveRunner(Client(handler));

        var reachable = await runner.ProbeReachabilityAsync();

        Assert.True(reachable);
    }

    [Fact]
    public async Task WhenTheExpectedToolIsInvoked_TheCasePasses()
    {
        var handler = new StubHandler(_ => JsonResponse(new { response = "ok", sessionId = "s1", toolsInvoked = new[] { "get_order_details" } }));
        var runner = new LiveRunner(Client(handler));

        var result = await runner.RunCaseAsync(Case());

        Assert.True(result.Passed);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task WhenADifferentToolIsInvoked_TheCaseFails()
    {
        var handler = new StubHandler(_ => JsonResponse(new { response = "ok", sessionId = "s1", toolsInvoked = new[] { "get_inventory_level" } }));
        var runner = new LiveRunner(Client(handler));

        var result = await runner.RunCaseAsync(Case());

        Assert.False(result.Passed);
        Assert.Equal(["get_inventory_level"], result.ToolsInvoked);
    }

    [Fact]
    public async Task WhenNoToolIsInvoked_TheCaseFails()
    {
        var handler = new StubHandler(_ => JsonResponse(new { response = "just chatting", sessionId = "s1", toolsInvoked = Array.Empty<string>() }));
        var runner = new LiveRunner(Client(handler));

        var result = await runner.RunCaseAsync(Case());

        Assert.False(result.Passed);
        Assert.Empty(result.ToolsInvoked);
    }

    [Fact]
    public async Task WhenTheRequestThrows_TheCaseFailsWithTheErrorRecorded_RatherThanThrowing()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("timed out"));
        var runner = new LiveRunner(Client(handler));

        var result = await runner.RunCaseAsync(Case());

        Assert.False(result.Passed);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task WhenTheResponseBodyIsNotValidJson_TheCaseFailsRatherThanAbortingTheRun()
    {
        // A response that returns 200 but a body ReadFromJsonAsync can't parse — e.g. AgentHost
        // behind a proxy returning an HTML error page — throws JsonException, which previously
        // fell outside RunCaseAsync's catch filter and aborted the entire run instead of just
        // this one case (contradicting the method's own FR-018 doc comment).
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>not json</html>", Encoding.UTF8, "application/json")
        });
        var runner = new LiveRunner(Client(handler));

        var result = await runner.RunCaseAsync(Case());

        Assert.False(result.Passed);
        Assert.NotNull(result.Error);
    }

    private static HttpResponseMessage JsonResponse(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };
}
