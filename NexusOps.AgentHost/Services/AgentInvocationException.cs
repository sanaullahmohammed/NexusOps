namespace NexusOps.AgentHost.Services;

/// <summary>
/// Raised when the agent fails to produce a response, carrying the session the request belonged to.
/// </summary>
/// <remarks>
/// 002 FR-005 requires the user's turn to be persisted even when the agent fails. If the session ID
/// was minted during that same request, a bare 500 left the caller with no way to name the session
/// the turn was written under, so it sat unreachable until its TTL expired. Carrying the identifier
/// on the exception lets the endpoint return it and the caller retry into the same conversation.
/// </remarks>
public sealed class AgentInvocationException(string sessionId, Exception innerException)
    : Exception("The agent failed to produce a response.", innerException)
{
    public string SessionId { get; } = sessionId;
}
