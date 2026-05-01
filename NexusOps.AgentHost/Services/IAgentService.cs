namespace NexusOps.AgentHost.Services;

public interface IAgentService
{
    Task<string> SendAsync(string prompt, CancellationToken cancellationToken = default);
}
