using NexusOps.AgentHost.Services;

namespace NexusOps.AgentHost.Endpoints;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/chat").WithTags("Chat");

        group.MapPost("/", async (ChatRequest request, IAgentService agentService, CancellationToken ct) =>
        {
            var response = await agentService.SendAsync(request.Prompt, ct);
            return Results.Ok(new ChatResponse(response));
        })
        .WithName("Chat")
        .WithSummary("Send a prompt to the agent")
        .WithDescription("Sends a natural language prompt to the Azure AI Foundry agent and returns the model's response. Creates a new thread per request (stateless).")
        .Produces<ChatResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }
}

/// <summary>A prompt to send to the AI agent.</summary>
/// <param name="Prompt">The natural language prompt.</param>
record ChatRequest(string Prompt);

/// <summary>The agent's response to the prompt.</summary>
/// <param name="Response">The model's reply.</param>
record ChatResponse(string Response);
