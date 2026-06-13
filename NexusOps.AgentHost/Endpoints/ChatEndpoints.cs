using NexusOps.AgentHost.Services;

namespace NexusOps.AgentHost.Endpoints;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/chat").WithTags("Chat");

        group.MapPost("/", async (ChatRequest request, IAgentService agentService, CancellationToken ct) =>
        {
            var (response, sessionId) = await agentService.SendAsync(request.Prompt, request.SessionId, ct);
            return Results.Ok(new ChatResponse(response, sessionId));
        })
        .WithName("Chat")
        .WithSummary("Send a prompt to the agent")
        .WithDescription("Sends a natural language prompt to the Azure AI Foundry agent and returns the model's response. Optionally supply a sessionId to continue a prior conversation — the agent will receive full conversation history. If sessionId is omitted, null, or empty, a new session is minted and returned. Sessions expire after 30 minutes of inactivity.")
        .Produces<ChatResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }
}

/// <summary>A prompt to send to the AI agent.</summary>
/// <param name="Prompt">The natural language prompt.</param>
/// <param name="SessionId">Optional session identifier. Absent, null, or empty mints a new session.</param>
record ChatRequest(string Prompt, string? SessionId = null);

/// <summary>The agent's response to the prompt.</summary>
/// <param name="Response">The model's reply.</param>
/// <param name="SessionId">The active session identifier — either the caller-supplied one or newly minted.</param>
record ChatResponse(string Response, string SessionId);
