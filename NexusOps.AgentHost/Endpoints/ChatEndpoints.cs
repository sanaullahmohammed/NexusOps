using NexusOps.AgentHost.Services;

namespace NexusOps.AgentHost.Endpoints;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/chat").WithTags("Chat");

        group.MapPost("/", async (ChatRequest request, IAgentService agentService, CancellationToken ct) =>
        {
            // Reject before minting a session or invoking the model: a malformed request should cost
            // nothing and leave nothing behind in the conversation store.
            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["prompt"] = ["A prompt is required and must not be empty or whitespace."]
                });
            }

            try
            {
                var (response, sessionId, toolsInvoked) = await agentService.SendAsync(request.Prompt, request.SessionId, ct);
                return Results.Ok(new ChatResponse(response, sessionId, toolsInvoked));
            }
            catch (AgentInvocationException ex)
            {
                // The user's turn was persisted under this session (002 FR-005). Return the
                // identifier so the caller can retry into the same conversation rather than
                // losing the turn to its TTL.
                // Deliberately does not advise resending the prompt: it is already in the session's
                // history, so resending would record it twice and the agent would see it twice.
                // Continue the conversation with this sessionId instead.
                return Results.Problem(
                    title: "The agent could not complete the request.",
                    detail: "The prompt was recorded in the session below. Continue with this sessionId; "
                          + "resending the same prompt would record it a second time.",
                    statusCode: StatusCodes.Status500InternalServerError,
                    extensions: new Dictionary<string, object?> { ["sessionId"] = ex.SessionId });
            }
        })
        .WithName("Chat")
        .WithSummary("Send a prompt to the agent")
        .WithDescription("Sends a natural language prompt to the Azure AI Foundry agent and returns the model's response. Optionally supply a sessionId to continue a prior conversation — the agent will receive full conversation history. If sessionId is omitted, null, or empty, a new session is minted and returned. An expired or unknown sessionId also mints a new one; if the conversation store is unreachable the supplied sessionId is preserved and the turn is processed without history. Sessions expire after 30 minutes of inactivity.")
        .Produces<ChatResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }
}

/// <summary>A prompt to send to the AI agent.</summary>
/// <param name="Prompt">The natural language prompt. Required; must not be empty or whitespace.</param>
/// <param name="SessionId">Optional session identifier. Absent, null, or empty mints a new session.</param>
record ChatRequest(string Prompt, string? SessionId = null);

/// <summary>The agent's response to the prompt.</summary>
/// <param name="Response">The model's reply.</param>
/// <param name="SessionId">The active session identifier — either the caller-supplied one or newly minted.</param>
/// <param name="ToolsInvoked">Names of the tools the agent invoked while producing this turn's response, in invocation order. Empty when no tool was invoked.</param>
record ChatResponse(string Response, string SessionId, IReadOnlyList<string> ToolsInvoked);
