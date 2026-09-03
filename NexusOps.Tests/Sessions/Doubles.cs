using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NexusOps.AgentHost.Configuration;
using NexusOps.AgentHost.Services;

namespace NexusOps.Tests.Sessions;

/// <summary>An <see cref="AIAgent"/> that answers from a supplied handler, recording what it was asked.</summary>
public sealed class FakeAgent(Func<IEnumerable<ChatMessage>, string> respond) : AIAgent
{
    /// <summary>An agent that always fails, for exercising the failure path.</summary>
    public static FakeAgent Failing(string message = "model unavailable") =>
        new(_ => throw new InvalidOperationException(message));

    public static FakeAgent Echoing(string reply = "ok") => new(_ => reply);

    /// <summary>An agent whose reply message carries a <see cref="FunctionCallContent"/> per named
    /// tool, for exercising 007's ToolsInvoked extraction without a real model call.</summary>
    public static FakeAgent InvokingTools(params string[] toolNames)
    {
        var agent = new FakeAgent(_ => "ok") { ToolNamesToReport = toolNames };
        return agent;
    }

    /// <summary>Tool names <see cref="RunCoreAsync"/> reports as invoked, via <see cref="InvokingTools"/>.</summary>
    private string[] ToolNamesToReport { get; init; } = [];

    /// <summary>The messages passed on the most recent call — history plus the new user turn.</summary>
    public List<ChatMessage> LastMessages { get; private set; } = [];

    public int CallCount { get; private set; }

    protected override Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        CancellationToken cancellationToken)
    {
        LastMessages = messages.ToList();
        CallCount++;
        var text = respond(LastMessages);

        var reply = new ChatMessage(ChatRole.Assistant, text);
        foreach (var toolName in ToolNamesToReport)
        {
            reply.Contents.Add(new FunctionCallContent(callId: Guid.NewGuid().ToString(), name: toolName, arguments: null!));
        }

        return Task.FromResult(new AgentResponse(reply));
    }

    protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session, JsonSerializerOptions? options, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement element, JsonSerializerOptions? options, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

/// <summary>
/// An <see cref="IConversationStore"/> whose retrieval outcome is dictated by the test, so that
/// "session absent" and "store unreachable" can be exercised separately.
/// </summary>
public sealed class FakeConversationStore : IConversationStore
{
    private readonly Dictionary<string, List<ConversationTurn>> _sessions = [];

    /// <summary>When true, every retrieval reports the store as unreachable.</summary>
    public bool IsUnavailable { get; set; }

    /// <summary>Session IDs passed to <see cref="AppendTurnsAsync"/>, in order.</summary>
    public List<string> AppendedTo { get; } = [];

    /// <summary>Number of times history retrieval was attempted.</summary>
    public int GetCallCount { get; private set; }

    public void Seed(string sessionId, params ConversationTurn[] turns) => _sessions[sessionId] = [.. turns];

    public IReadOnlyList<ConversationTurn> TurnsFor(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var turns) ? turns : [];

    public Task<HistoryResult> GetHistoryAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        GetCallCount++;

        if (IsUnavailable)
            return Task.FromResult(HistoryResult.Unavailable());

        return Task.FromResult(_sessions.TryGetValue(sessionId, out var turns)
            ? HistoryResult.Found(turns)
            : HistoryResult.Missing());
    }

    public Task AppendTurnsAsync(string sessionId, IReadOnlyList<ConversationTurn> newTurns, ConversationSessionOptions options, CancellationToken cancellationToken = default)
    {
        AppendedTo.Add(sessionId);

        if (IsUnavailable)
            return Task.CompletedTask;   // degraded: the write is dropped, silently, by design

        if (!_sessions.TryGetValue(sessionId, out var turns))
        {
            turns = [];
            _sessions[sessionId] = turns;
        }

        turns.AddRange(newTurns);

        if (turns.Count > options.MaxTurns)
            turns.RemoveRange(0, turns.Count - options.MaxTurns);

        return Task.CompletedTask;
    }

    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _sessions.Remove(sessionId);
        return Task.CompletedTask;
    }
}
