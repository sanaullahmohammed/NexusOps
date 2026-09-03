using System.Diagnostics.CodeAnalysis;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using NexusOps.AgentHost.Configuration;

namespace NexusOps.AgentHost.Services;

public sealed class AgentService : IAgentService
{
    private readonly AIAgent _agent;
    private readonly IConversationStore _store;
    private readonly IOptions<ConversationSessionOptions> _sessionOptions;
    private readonly ILogger<AgentService> _logger;

    public AgentService(AIAgent agent, IConversationStore store, IOptions<ConversationSessionOptions> sessionOptions, ILogger<AgentService> logger)
    {
        _agent = agent;
        _store = store;
        _sessionOptions = sessionOptions;
        _logger = logger;
    }

    public async Task<(string Response, string SessionId, IReadOnlyList<string> ToolsInvoked)> SendAsync(string prompt, string? sessionId, CancellationToken cancellationToken = default)
    {
        var options = _sessionOptions.Value;
        var now = DateTimeOffset.UtcNow;

        var (activeSessionId, history) = await ResolveSessionAsync(sessionId, now, cancellationToken);

        var messages = new List<ChatMessage>(history.Count + 1);
        foreach (var turn in history)
        {
            var role = turn.Role == "assistant" ? ChatRole.Assistant : ChatRole.User;
            messages.Add(new ChatMessage(role, turn.Content));
        }
        messages.Add(new ChatMessage(ChatRole.User, prompt));

        var userTurn = new ConversationTurn("user", prompt, now);

        string responseText;
        IReadOnlyList<string> toolsInvoked;
        try
        {
            var agentResponse = await _agent.RunAsync(messages, session: null, options: null, cancellationToken);
            responseText = agentResponse.ToString();
            toolsInvoked = ExtractToolsInvoked(agentResponse.Messages);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller hung up, or the host is shutting down. Neither is an agent failure, and
            // reporting it as one would inflate failure metrics and return a 500 to nobody.
            throw;
        }
        catch (Exception ex)
        {
            // 002 FR-005: persist the user turn even when the agent fails; do not persist a failed
            // assistant turn. The session ID travels with the exception so the caller receives the
            // identifier that turn was written under — otherwise it is unreachable until its TTL.
            await PersistAsync(activeSessionId, [userTurn], options);
            throw new AgentInvocationException(activeSessionId, ex);
        }

        var assistantTurn = new ConversationTurn("assistant", responseText, DateTimeOffset.UtcNow);
        await PersistAsync(activeSessionId, [userTurn, assistantTurn], options);

        var savedCount = Math.Min(history.Count + 2, options.MaxTurns);
        _logger.LogDebug("session.history_saved {SessionIdPrefix} {TurnCount} {Timestamp}", SessionLogToken.For(activeSessionId), savedCount, DateTimeOffset.UtcNow);

        return (responseText, activeSessionId, toolsInvoked);
    }

    /// <summary>
    /// The Microsoft Agent Framework already records every tool call this turn made as
    /// <see cref="FunctionCallContent"/> items on the response's messages — this is a pure
    /// projection over data already returned, not a second model call.
    /// </summary>
    private static IReadOnlyList<string> ExtractToolsInvoked(IEnumerable<ChatMessage> messages) =>
        [.. messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionCallContent>()
            .Select(c => c.Name)];

    /// <summary>
    /// Determines which session this request belongs to and what history it starts from.
    /// </summary>
    private async Task<(string SessionId, IReadOnlyList<ConversationTurn> History)> ResolveSessionAsync(
        string? suppliedSessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // No identifier supplied: mint one. Reading the store first would be a guaranteed miss
        // against a key created microseconds ago, and would log a history load that never happened.
        //
        // A malformed identifier is treated the same way (002 FR-007, and the clarification at
        // spec.md:78). This also keeps caller-controlled text out of the Redis key space and out of
        // anything echoed back — the identifier is returned on the store-unavailable path and in the
        // 500 body, so it must be a value this service minted, not arbitrary input.
        if (!IsWellFormed(suppliedSessionId))
        {
            return (MintSession(now), []);
        }

        var result = await _store.GetHistoryAsync(suppliedSessionId, cancellationToken);

        switch (result.Outcome)
        {
            case HistoryOutcome.Found:
                _logger.LogDebug("session.history_loaded {SessionIdPrefix} {TurnCount} {Timestamp}", SessionLogToken.For(suppliedSessionId), result.Turns.Count, now);
                return (suppliedSessionId, result.Turns);

            case HistoryOutcome.Missing:
                // 002 FR-007: an expired, unknown or malformed ID starts a fresh session rather
                // than surfacing an error.
                return (MintSession(now), []);

            case HistoryOutcome.Unavailable:
            default:
                // 003 FR-009: the store is unreachable, so whether this session exists is unknown.
                // Keep the caller's identifier and run statelessly; the conversation resumes intact
                // once the store recovers. The store has already logged session.degraded.
                return (suppliedSessionId, []);
        }
    }

    /// <summary>
    /// A session identifier is well formed only if this service could have minted it: a UUID in the
    /// canonical hyphenated form, matching the opaque-token format the chat contract publishes.
    /// </summary>
    private static bool IsWellFormed([NotNullWhen(true)] string? sessionId) =>
        !string.IsNullOrWhiteSpace(sessionId) && Guid.TryParseExact(sessionId, "D", out _);

    /// <summary>
    /// Writes turns to the store using a token that is deliberately NOT the request's.
    /// </summary>
    /// <remarks>
    /// The endpoint passes <c>HttpContext.RequestAborted</c>. Handing that to the store means a
    /// client disconnect cancels the write — on the failure path that silently defeats 002 FR-005,
    /// whose entire purpose is to survive the failure, and the swallowed cancellation surfaces as a
    /// <c>session.degraded</c> warning blaming Redis for something Redis did not do. On the success
    /// path it discards both turns after the model call has already been paid for. Persistence is
    /// cheap, bounded, and worth completing after the caller has gone.
    /// </remarks>
    private Task PersistAsync(string sessionId, IReadOnlyList<ConversationTurn> turns, ConversationSessionOptions options) =>
        _store.AppendTurnsAsync(sessionId, turns, options, CancellationToken.None);

    private string MintSession(DateTimeOffset now)
    {
        var sessionId = Guid.NewGuid().ToString();
        _logger.LogInformation("session.created {SessionIdPrefix} {Timestamp}", SessionLogToken.For(sessionId), now);
        return sessionId;
    }
}
