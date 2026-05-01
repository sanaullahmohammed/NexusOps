using Microsoft.Agents.AI;

namespace NexusOps.AgentHost.Services;

public sealed class AgentService : IAgentService
{
    private readonly AIAgent _agent;

    public AgentService(AIAgent agent)
    {
        _agent = agent;
    }

    public async Task<string> SendAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var response = await _agent.RunAsync(prompt, cancellationToken: cancellationToken);
        return response.ToString();
    }
}
