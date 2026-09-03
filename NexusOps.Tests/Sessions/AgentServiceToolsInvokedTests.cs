using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NexusOps.AgentHost.Configuration;
using NexusOps.AgentHost.Services;

namespace NexusOps.Tests.Sessions;

/// <summary>
/// Covers 007 FR-012: the tool(s) a turn invoked are recovered from the same <c>AgentResponse</c>
/// the reply text already comes from, per contracts/agent-chat-response.md.
/// </summary>
public class AgentServiceToolsInvokedTests
{
    private const string CallerSessionId = "11111111-2222-3333-4444-555555555555";

    private static AgentService Create(FakeAgent agent) =>
        new(agent,
            new FakeConversationStore(),
            Options.Create(new ConversationSessionOptions()),
            NullLogger<AgentService>.Instance);

    [Fact]
    public async Task WhenTheAgentInvokesATool_ItsNameIsReturned()
    {
        var agent = FakeAgent.InvokingTools("investigate_order_root_cause");

        var (_, _, toolsInvoked) = await Create(agent).SendAsync("why is ORD-0001 delayed?", CallerSessionId);

        Assert.Equal(["investigate_order_root_cause"], toolsInvoked);
    }

    [Fact]
    public async Task WhenTheAgentInvokesNoTool_ToolsInvokedIsEmptyNotNull()
    {
        var agent = FakeAgent.Echoing("just chatting");

        var (_, _, toolsInvoked) = await Create(agent).SendAsync("hello", CallerSessionId);

        Assert.NotNull(toolsInvoked);
        Assert.Empty(toolsInvoked);
    }

    [Fact]
    public async Task WhenTheAgentInvokesMultipleTools_AllNamesAreReturnedInOrder()
    {
        var agent = FakeAgent.InvokingTools("get_order_details", "get_inventory_level");

        var (_, _, toolsInvoked) = await Create(agent).SendAsync("compound question", CallerSessionId);

        Assert.Equal(["get_order_details", "get_inventory_level"], toolsInvoked);
    }
}
