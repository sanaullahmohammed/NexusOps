using MassTransit;
using NexusOps.Contracts.Dtos;
using NexusOps.Contracts.Messages;

namespace NexusOps.WorkflowOrchestrator.OrderAction;

/// <summary>
/// This system's first mutating, approval-gated saga (constitution Principle III). Owns persisted
/// state and finalization only — validation and execution are done by
/// <see cref="OrderActionValidationConsumer"/> and <see cref="OrderActionExecutionConsumer"/>
/// respectively, mirroring feature 005's saga/consumer split. Nothing is ever mutated except via
/// the <c>AwaitingApproval</c> → (<c>Approve</c>) → <c>Executing</c> path; a <c>Reject</c> or a
/// not-found validation are both dead ends that never publish <see cref="BeginOrderActionExecution"/>.
/// </summary>
public sealed class OrderActionSaga : MassTransitStateMachine<OrderActionSagaState>
{
    public State Validating { get; private set; } = null!;
    public State AwaitingApproval { get; private set; } = null!;
    public State Executing { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Rejected { get; private set; } = null!;
    public State Failed { get; private set; } = null!;

    public Event<RequestOrderRefund> RefundRequested { get; private set; } = null!;
    public Event<RequestOrderCancellation> CancellationRequested { get; private set; } = null!;
    public Event<ActionValidationCompleted> ValidationCompleted { get; private set; } = null!;
    public Event<ApproveOrderAction> Approve { get; private set; } = null!;
    public Event<RejectOrderAction> Reject { get; private set; } = null!;
    public Event<OrderActionExecutionCompleted> ExecutionCompleted { get; private set; } = null!;

    public OrderActionSaga()
    {
        InstanceState(x => x.CurrentState);

        // Neither request carries a correlation id of its own -- every request starts a brand-new
        // action, so a fresh CorrelationId is minted right here (matches 005's InvestigateOrderRootCause).
        Event(() => RefundRequested, x => x.CorrelateById(context => Guid.NewGuid()));
        Event(() => CancellationRequested, x => x.CorrelateById(context => Guid.NewGuid()));

        Event(() => ValidationCompleted, x =>
        {
            x.CorrelateById(context => context.Message.CorrelationId);
            x.OnMissingInstance(m => m.Discard());
        });

        Event(() => ExecutionCompleted, x =>
        {
            x.CorrelateById(context => context.Message.CorrelationId);
            x.OnMissingInstance(m => m.Discard());
        });

        // Approve/Reject are request/response -- a missing instance still owes the caller a reply,
        // so OnMissingInstance responds NotFound rather than discarding (unlike the two events above).
        Event(() => Approve, x =>
        {
            x.CorrelateById(context => context.Message.ApprovalReference);
            x.OnMissingInstance(m => m.ExecuteAsync(RespondNotFoundAsync));
        });
        Event(() => Reject, x =>
        {
            x.CorrelateById(context => context.Message.ApprovalReference);
            x.OnMissingInstance(m => m.ExecuteAsync(RespondNotFoundAsync));
        });

        Initially(
            When(RefundRequested)
                .Then(context =>
                {
                    context.Saga.ActionType = OrderActionType.Refund;
                    context.Saga.OrderId = context.Message.OrderId;
                    context.Saga.Amount = context.Message.Amount;
                    context.Saga.Reason = context.Message.Reason;
                    context.Saga.RequestResponseAddress = context.ResponseAddress;
                    context.Saga.RequestRequestId = context.RequestId;
                    context.Saga.RequestedAt = DateTimeOffset.UtcNow;
                })
                .Publish(context => new BeginActionValidation(context.Saga.CorrelationId, context.Saga.OrderId))
                .TransitionTo(Validating),
            When(CancellationRequested)
                .Then(context =>
                {
                    context.Saga.ActionType = OrderActionType.Cancellation;
                    context.Saga.OrderId = context.Message.OrderId;
                    context.Saga.Reason = context.Message.Reason;
                    context.Saga.RequestResponseAddress = context.ResponseAddress;
                    context.Saga.RequestRequestId = context.RequestId;
                    context.Saga.RequestedAt = DateTimeOffset.UtcNow;
                })
                .Publish(context => new BeginActionValidation(context.Saga.CorrelationId, context.Saga.OrderId))
                .TransitionTo(Validating));

        During(Validating,
            When(ValidationCompleted)
                .ThenAsync(HandleValidationCompletedAsync));

        During(AwaitingApproval,
            When(Approve)
                .Then(context =>
                {
                    context.Saga.DecidedAt = DateTimeOffset.UtcNow;
                    context.Saga.ApprovalResponseAddress = context.ResponseAddress;
                    context.Saga.ApprovalRequestId = context.RequestId;
                })
                .Publish(context => new BeginOrderActionExecution(
                    context.Saga.CorrelationId, context.Saga.ActionType, context.Saga.OrderId, context.Saga.Amount))
                .TransitionTo(Executing),
            When(Reject)
                .ThenAsync(HandleRejectAsync));

        During(Executing,
            When(ExecutionCompleted)
                .ThenAsync(HandleExecutionCompletedAsync));

        // Once approved (Executing) or resolved (Completed/Rejected), a second Approve/Reject is
        // unconditionally "already decided" -- the decision, whatever it was, cannot be revisited
        // (spec.md FR-008/FR-009, SC-008).
        During(Executing, Completed, Rejected,
            When(Approve).Respond(AlreadyDecidedResult),
            When(Reject).Respond(AlreadyDecidedResult));

        // Failed is reached two different ways that must answer a later Approve/Reject differently:
        // (a) the order was never found at validation -- ExecutionOutcome is still null, nothing was
        //     ever decided, so the reference was never really approvable (respond NotFound, matching
        //     "this reference doesn't exist as an actionable one" rather than claiming a decision
        //     that never happened); (b) execution itself failed after a real approval -- ExecutionOutcome
        //     is set, so this *was* decided (respond AlreadyDecided, same as every other terminal state).
        During(Failed,
            When(Approve).Respond(context => context.Saga.ExecutionOutcome is null
                ? NotFoundResult(context.Message.ApprovalReference)
                : AlreadyDecidedResult(context)),
            When(Reject).Respond(context => context.Saga.ExecutionOutcome is null
                ? NotFoundResult(context.Message.ApprovalReference)
                : AlreadyDecidedResult(context)));
    }

    private async Task HandleValidationCompletedAsync(BehaviorContext<OrderActionSagaState, ActionValidationCompleted> context)
    {
        var saga = context.Saga;
        var found = context.Message.Status == SourceFindingStatus.Succeeded && context.Message.Order is not null;

        OrderActionRequestResult result;
        if (found)
        {
            if (saga.ActionType == OrderActionType.Refund && saga.Amount is null)
            {
                saga.Amount = context.Message.Order!.TotalAmount;
            }

            result = new OrderActionRequestResult(saga.OrderId, saga.ActionType, saga.CorrelationId, OrderActionStatus.AwaitingApproval, saga.Amount);
        }
        else
        {
            // A confirmed NotFound is a completed, trustworthy validation result; Unavailable/TimedOut
            // means the question was never actually answered and the caller should retry, not be told
            // the order doesn't exist (mirrors 005's SourceFindingStatus distinction; regression found
            // in code review -- this used to collapse both into NotFound).
            var status = context.Message.Status == SourceFindingStatus.NotFound
                ? OrderActionStatus.NotFound
                : OrderActionStatus.Unavailable;
            result = new OrderActionRequestResult(saga.OrderId, saga.ActionType, saga.CorrelationId, status, null);
        }

        // The saga responds from whatever consume context happens to finalize validation -- never
        // the original request's context -- so the reply is sent explicitly to the address and
        // RequestId captured when RequestOrderRefund/RequestOrderCancellation was first consumed
        // (mirrors 005's research.md Decision 2).
        if (saga.RequestResponseAddress is not null)
        {
            var endpoint = await context.GetSendEndpoint(saga.RequestResponseAddress);
            await endpoint.Send(result, sendContext => sendContext.RequestId = saga.RequestRequestId);
            saga.RequestResponseAddress = null;
        }

        if (found)
        {
            await context.TransitionToState(AwaitingApproval);
        }
        else
        {
            // No notification is published here: nothing was ever pending a human decision, and the
            // requester already received the NotFound answer synchronously above.
            await context.TransitionToState(Failed);
        }
    }

    private async Task HandleRejectAsync(BehaviorContext<OrderActionSagaState, RejectOrderAction> context)
    {
        var saga = context.Saga;
        saga.DecidedAt = DateTimeOffset.UtcNow;
        saga.CompletedAt = DateTimeOffset.UtcNow;

        var actionLabel = saga.ActionType == OrderActionType.Refund ? "refund" : "cancellation";
        var message = $"The {actionLabel} for order {saga.OrderId} was rejected.";

        await context.RespondAsync(new OrderActionDecisionResult(
            saga.CorrelationId, OrderActionDecisionOutcome.Rejected, null, message));

        await context.Publish(new NotificationRequested(
            saga.CorrelationId, saga.OrderId, saga.ActionType.ToString(), "Rejected", message));

        await context.TransitionToState(Rejected);
    }

    private async Task HandleExecutionCompletedAsync(BehaviorContext<OrderActionSagaState, OrderActionExecutionCompleted> context)
    {
        var saga = context.Saga;
        saga.ExecutionOutcome = context.Message.Outcome;
        saga.PriorStatus = context.Message.PriorStatus;
        saga.CompletedAt = DateTimeOffset.UtcNow;

        if (saga.ApprovalResponseAddress is not null)
        {
            var endpoint = await context.GetSendEndpoint(saga.ApprovalResponseAddress);
            await endpoint.Send(
                new OrderActionDecisionResult(saga.CorrelationId, OrderActionDecisionOutcome.Approved, saga.ExecutionOutcome, context.Message.Detail),
                sendContext => sendContext.RequestId = saga.ApprovalRequestId);
            saga.ApprovalResponseAddress = null;
        }

        await context.Publish(new NotificationRequested(
            saga.CorrelationId, saga.OrderId, saga.ActionType.ToString(), saga.ExecutionOutcome.ToString()!, context.Message.Detail));

        var nextState = saga.ExecutionOutcome == OrderActionExecutionOutcome.Executed ? Completed : Failed;
        await context.TransitionToState(nextState);
    }

    private static OrderActionDecisionResult AlreadyDecidedResult<TMessage>(BehaviorContext<OrderActionSagaState, TMessage> context)
        where TMessage : class
    {
        var reference = context.Message switch
        {
            ApproveOrderAction approve => approve.ApprovalReference,
            RejectOrderAction reject => reject.ApprovalReference,
            _ => context.Saga.CorrelationId
        };

        // A retried Approve that lands after the first attempt already finished (Completed/Failed)
        // has a real outcome sitting right here on the saga -- surface it instead of always
        // responding null, which previously left a legitimate retry no better informed than a
        // decision made by someone else entirely (code review finding). Still null while genuinely
        // Executing (no outcome exists yet) or Rejected (no execution ever happened).
        var executionOutcome = context.Saga.ExecutionOutcome;
        var message = executionOutcome is null
            ? "This action has already been decided."
            : $"This action has already been decided. Outcome: {executionOutcome}.";
        return new OrderActionDecisionResult(reference, OrderActionDecisionOutcome.AlreadyDecided, executionOutcome, message);
    }

    private static OrderActionDecisionResult NotFoundResult(Guid approvalReference) =>
        new(approvalReference, OrderActionDecisionOutcome.NotFound, null, "No pending action exists for this reference.");

    private static Task RespondNotFoundAsync<TMessage>(ConsumeContext<TMessage> context) where TMessage : class
    {
        var reference = context.Message switch
        {
            ApproveOrderAction approve => approve.ApprovalReference,
            RejectOrderAction reject => reject.ApprovalReference,
            _ => Guid.Empty
        };
        return context.RespondAsync(NotFoundResult(reference));
    }
}
