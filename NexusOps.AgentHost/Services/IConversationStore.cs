using NexusOps.AgentHost.Configuration;

namespace NexusOps.AgentHost.Services;

public interface IConversationStore
{
    /// <summary>
    /// Retrieves a session's history, reporting whether the session was found, is genuinely absent,
    /// or could not be read. Callers MUST distinguish the last two: an absent session may be replaced,
    /// an unreachable store must not cause one.
    /// </summary>
    Task<HistoryResult> GetHistoryAsync(string sessionId, CancellationToken cancellationToken = default);
    Task AppendTurnsAsync(string sessionId, IReadOnlyList<ConversationTurn> newTurns, ConversationSessionOptions options, CancellationToken cancellationToken = default);
    Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}
