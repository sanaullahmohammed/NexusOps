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
        bool callerSuppliedId = !string.IsNullOrWhiteSpace(sessionId);

        if (!callerSuppliedId)
        {
            sessionId = Guid.NewGuid().ToString();
            _logger.LogInformation("session.created {SessionIdPrefix} {Timestamp}", sessionId[..8], now);
        }

        var history = await _store.GetHistoryAsync(sessionId!, cancellationToken);

        // FR-007: empty history for a caller-supplied ID means expired/unknown — mint new
        if (callerSuppliedId && history.Count == 0)
        {
            sessionId = Guid.NewGuid().ToString();
            _logger.LogInformation("session.created {SessionIdPrefix} {Timestamp}", sessionId[..8], now);
        }
        else if (!callerSuppliedId || history.Count > 0)
        {
            _logger.LogDebug("session.history_loaded {SessionIdPrefix} {TurnCount} {Timestamp}", sessionId![..8], history.Count, now);
        }

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
        catch
        {
            // FR-005: persist the user turn even when the agent fails; do not persist a failed assistant turn
            await _store.AppendTurnsAsync(sessionId!, [userTurn], options, cancellationToken);
            throw;
        }

        var assistantTurn = new ConversationTurn("assistant", responseText, DateTimeOffset.UtcNow);
        await _store.AppendTurnsAsync(sessionId!, [userTurn, assistantTurn], options, cancellationToken);

        var savedCount = Math.Min(history.Count + 2, options.MaxTurns);
        _logger.LogDebug("session.history_saved {SessionIdPrefix} {TurnCount} {Timestamp}", sessionId![..8], savedCount, DateTimeOffset.UtcNow);

        return (responseText, sessionId!);
    }
}
