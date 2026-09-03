using MassTransit;
using NexusOps.Contracts.Messages;

namespace NexusOps.AgentHost.Endpoints;

/// <summary>
/// The only path to a decision on a pending refund/cancellation request — deliberately not an
/// agent tool (Constitution Principle III: approval is a human decision, independent of the chat
/// agent). Called directly, e.g. via <c>curl</c>, per <c>ROADMAP.md</c>'s locked "no UI" decision.
/// </summary>
public static class ApprovalEndpoints
{
    public static IEndpointRouteBuilder MapApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/approvals").WithTags("Approvals");

        group.MapPost("/{id:guid}/approve", async (Guid id, IRequestClient<ApproveOrderAction> client, CancellationToken ct) =>
        {
            try
            {
                // Blocks until the saga's execution consumer reports a final outcome — the
                // response carries the real result, not an interim acknowledgment
                // (research.md Decision 3; timeout budget in contracts/saga-message-contracts.md).
                var response = await client.GetResponse<OrderActionDecisionResult>(new ApproveOrderAction(id), ct);
                return Results.Ok(response.Message);
            }
            catch (RequestTimeoutException)
            {
                // The decision was recorded (the saga transitioned out of AwaitingApproval before
                // execution even began) -- the caller just did not see the outcome in time.
                return Results.Problem(
                    title: "Timed out waiting for execution to finish.",
                    detail: $"The approval for reference {id} was recorded, but execution did not finish within the timeout. Check the order directly for its current state.",
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }
        })
        .WithName("ApproveOrderAction")
        .WithSummary("Approve a pending refund or cancellation")
        .WithDescription("Executes the requested mutation and returns the real outcome (Executed, Failed, or FailedAndCompensated), or reports AlreadyDecided/NotFound.")
        .Produces<OrderActionDecisionResult>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        group.MapPost("/{id:guid}/reject", async (Guid id, string? reason, IRequestClient<RejectOrderAction> client, CancellationToken ct) =>
        {
            try
            {
                var response = await client.GetResponse<OrderActionDecisionResult>(new RejectOrderAction(id, reason), ct);
                return Results.Ok(response.Message);
            }
            catch (RequestTimeoutException)
            {
                // Reject responds within the same consume that receives it (no execution to wait
                // for), so a timeout here means the saga itself was unreachable/overloaded, not that
                // a decision is pending elsewhere — distinct wording from /approve's timeout, and
                // previously unhandled entirely, surfacing as an unstyled 500 (code review finding).
                return Results.Problem(
                    title: "Timed out waiting for the workflow orchestrator.",
                    detail: $"The rejection for reference {id} could not be confirmed within the timeout. It is safe to retry — a rejection is only ever applied once.",
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }
        })
        .WithName("RejectOrderAction")
        .WithSummary("Reject a pending refund or cancellation")
        .WithDescription("Permanently prevents the requested mutation from ever executing. Responds immediately.")
        .Produces<OrderActionDecisionResult>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        return app;
    }
}
