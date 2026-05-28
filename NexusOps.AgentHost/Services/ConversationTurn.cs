namespace NexusOps.AgentHost.Services;

/// <summary>A single message within a conversation session.</summary>
/// <param name="Role">Speaker role: "user" or "assistant".</param>
/// <param name="Content">Message content.</param>
/// <param name="Timestamp">When the message was recorded.</param>
public sealed record ConversationTurn(string Role, string Content, DateTimeOffset Timestamp);
