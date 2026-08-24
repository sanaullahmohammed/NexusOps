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

    public async Task<(string Response, string SessionId)> SendAsync(string prompt, string? sessionId, CancellationToken cancellationToken = default)
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
        try
        {
            var agentResponse = await _agent.RunAsync(messages, session: null, options: null, cancellationToken);
            responseText = agentResponse.ToString();
        }
        catch (Exception ex)
        {
            // 002 FR-005: persist the user turn even when the agent fails; do not persist a failed
            // assistant turn. The session ID travels with the exception so the caller receives the
            // identifier that turn was written under — otherwise it is unreachable until its TTL.
            await _store.AppendTurnsAsync(activeSessionId, [userTurn], options, cancellationToken);
            throw new AgentInvocationException(activeSessionId, ex);
        }

        var assistantTurn = new ConversationTurn("assistant", responseText, DateTimeOffset.UtcNow);
        await _store.AppendTurnsAsync(activeSessionId, [userTurn, assistantTurn], options, cancellationToken);

        var savedCount = Math.Min(history.Count + 2, options.MaxTurns);
        _logger.LogDebug("session.history_saved {SessionIdPrefix} {TurnCount} {Timestamp}", SessionLogToken.For(activeSessionId), savedCount, DateTimeOffset.UtcNow);

        return (responseText, activeSessionId);
    }

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
        if (string.IsNullOrWhiteSpace(suppliedSessionId))
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

    private string MintSession(DateTimeOffset now)
    {
        var sessionId = Guid.NewGuid().ToString();
        _logger.LogInformation("session.created {SessionIdPrefix} {Timestamp}", SessionLogToken.For(sessionId), now);
        return sessionId;
    }
}
