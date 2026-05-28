using NexusOps.AgentHost.Configuration;

namespace NexusOps.AgentHost.Services;

public interface IConversationStore
{
    Task<IReadOnlyList<ConversationTurn>> GetHistoryAsync(string sessionId, CancellationToken cancellationToken = default);
    Task AppendTurnsAsync(string sessionId, IReadOnlyList<ConversationTurn> newTurns, ConversationSessionOptions options, CancellationToken cancellationToken = default);
    Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}
