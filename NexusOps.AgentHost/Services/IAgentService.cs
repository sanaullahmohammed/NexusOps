namespace NexusOps.AgentHost.Services;

public interface IAgentService
{
    Task<(string Response, string SessionId, IReadOnlyList<string> ToolsInvoked)> SendAsync(string prompt, string? sessionId, CancellationToken cancellationToken = default);
}
