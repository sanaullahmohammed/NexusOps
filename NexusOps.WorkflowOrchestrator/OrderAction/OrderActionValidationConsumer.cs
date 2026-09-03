using MassTransit;
using NexusOps.Contracts.Dtos;
using NexusOps.Contracts.Messages;

namespace NexusOps.WorkflowOrchestrator.OrderAction;

/// <summary>
/// Confirms the order named by a refund/cancellation request actually exists before the saga ever
/// parks it in <c>AwaitingApproval</c> (spec.md User Story 1 Acceptance Scenario 3). Reuses feature
/// 005's <c>RequestOrderFinding</c>/<c>OrderFindingReported</c> request/response contract verbatim
/// rather than inventing a second order-lookup contract, but publishes a new, 006-owned event
/// (<see cref="ActionValidationCompleted"/>) rather than re-broadcasting the shared
/// <c>OrderFindingReported</c> event, so this feature's validation traffic never reaches feature
/// 005's <c>OrderInvestigationSaga</c> queue (research.md Decision 1, implementation note).
/// </summary>
public sealed class OrderActionValidationConsumer(IRequestClient<RequestOrderFinding> orderClient) : IConsumer<BeginActionValidation>
{
    private static readonly RequestTimeout PerLegTimeout = RequestTimeout.After(s: 5);

    public async Task Consume(ConsumeContext<BeginActionValidation> context)
    {
        var correlationId = context.Message.CorrelationId;

        SourceFindingStatus status;
        OrderSummary? order;

        try
        {
            var response = await orderClient.GetResponse<OrderFindingReported>(
                new RequestOrderFinding(correlationId, context.Message.OrderId), context.CancellationToken, PerLegTimeout);
            status = response.Message.Status;
            order = response.Message.Order;
        }
        catch (RequestTimeoutException)
        {
            status = SourceFindingStatus.TimedOut;
            order = null;
        }
        catch (Exception)
        {
            status = SourceFindingStatus.Unavailable;
            order = null;
        }

        await context.Publish(new ActionValidationCompleted(correlationId, status, order));
    }
}
