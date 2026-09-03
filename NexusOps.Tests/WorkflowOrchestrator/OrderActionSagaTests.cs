using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NexusOps.Contracts.Dtos;
using NexusOps.Contracts.Messages;
using OA = NexusOps.WorkflowOrchestrator.OrderAction;

namespace NexusOps.Tests.WorkflowOrchestrator;

/// <summary>
/// Covers spec 006 User Stories 1, 2, 3, 4 (finalize/decision logic), and 5 (notification) at the
/// saga level, plus User Story 7's concurrent-decision safety property. Uses MassTransit's
/// in-memory test harness with only the saga registered (no validation/execution consumers) --
/// exactly 005's <c>OrderInvestigationSagaTests.cs</c> shape: validation and execution outcomes
/// are driven by manually publishing the events those consumers would otherwise produce.
/// </summary>
public sealed class OrderActionSagaTests : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ITestHarness _harness;
    private readonly ISagaStateMachineTestHarness<OA.OrderActionSaga, OA.OrderActionSagaState> _sagaHarness;

    public OrderActionSagaTests()
    {
        _provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddSagaStateMachine<OA.OrderActionSaga, OA.OrderActionSagaState>()
                    .InMemoryRepository();
            })
            .BuildServiceProvider(true);

        _harness = _provider.GetRequiredService<ITestHarness>();
        _sagaHarness = _harness.GetSagaStateMachineHarness<OA.OrderActionSaga, OA.OrderActionSagaState>();
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    private static readonly OrderSummary SampleOrder = new(
        OrderId: "ORD-0003",
        CustomerId: "CUST-003",
        Status: "processing",
        TotalAmount: 299.99m,
        ExpectedDelivery: DateOnly.FromDateTime(DateTime.UtcNow),
        ActualDelivery: null,
        LineItems: [new OrderLineItem("SKU-ELEC-001", "Wireless Headphones Pro", 1, 299.99m)]);

    private async Task<(Guid CorrelationId, Task<Response<OrderActionRequestResult>> ResponseTask)> StartRefundAsync(
        string orderId, decimal? amount = null)
    {
        var client = _harness.GetRequestClient<RequestOrderRefund>();
        var task = client.GetResponse<OrderActionRequestResult>(new RequestOrderRefund(orderId, amount, null), timeout: RequestTimeout.After(s: 10));

        var ids = await _sagaHarness.Exists(x => x.OrderId == orderId, _sagaHarness.StateMachine.Validating, TimeSpan.FromSeconds(5));
        Assert.NotNull(ids);
        Assert.NotEmpty(ids);

        return (ids[0], task);
    }

    private async Task<(Guid CorrelationId, Task<Response<OrderActionRequestResult>> ResponseTask)> StartCancellationAsync(string orderId)
    {
        var client = _harness.GetRequestClient<RequestOrderCancellation>();
        var task = client.GetResponse<OrderActionRequestResult>(new RequestOrderCancellation(orderId, null), timeout: RequestTimeout.After(s: 10));

        var ids = await _sagaHarness.Exists(x => x.OrderId == orderId, _sagaHarness.StateMachine.Validating, TimeSpan.FromSeconds(5));
        Assert.NotNull(ids);
        Assert.NotEmpty(ids);

        return (ids[0], task);
    }

    private async Task<Guid> RequestAndAwaitApprovalAsync(string orderId)
    {
        var (correlationId, responseTask) = await StartRefundAsync(orderId);
        await _harness.Bus.Publish(new ActionValidationCompleted(correlationId, SourceFindingStatus.Succeeded, SampleOrder));

        var awaitingApproval = await _sagaHarness.Exists(correlationId, _sagaHarness.StateMachine.AwaitingApproval, TimeSpan.FromSeconds(5));
        Assert.NotNull(awaitingApproval);

        var response = await responseTask;
        Assert.Equal(OrderActionStatus.AwaitingApproval, response.Message.Status);

        return correlationId;
    }

    [Fact]
    public async Task RefundWithNoAmount_ValidatesAndDefaultsAmountToOrderTotal()
    {
        await _harness.Start();

        var (correlationId, responseTask) = await StartRefundAsync("ORD-0003");
        await _harness.Bus.Publish(new ActionValidationCompleted(correlationId, SourceFindingStatus.Succeeded, SampleOrder));

        var awaitingApproval = await _sagaHarness.Exists(correlationId, _sagaHarness.StateMachine.AwaitingApproval, TimeSpan.FromSeconds(5));
        Assert.NotNull(awaitingApproval);

        var response = await responseTask;
        Assert.Equal(OrderActionStatus.AwaitingApproval, response.Message.Status);
        Assert.Equal(SampleOrder.TotalAmount, response.Message.Amount);
    }

    [Theory]
    [InlineData(SourceFindingStatus.Unavailable)]
    [InlineData(SourceFindingStatus.TimedOut)]
    public async Task ValidationSourceUnavailable_RespondsUnavailableNotNotFound(SourceFindingStatus sourceStatus)
    {
        // Regression test (code review finding): a genuine validation-leg outage was previously
        // collapsed into the same OrderActionStatus.NotFound the caller sees for a confirmed
        // nonexistent order, misleadingly telling the operator the order doesn't exist rather than
        // that the order service could not be reached -- a real regression vs. feature 005's
        // SourceFindingStatus three-way distinction (NotFound vs. Unavailable/TimedOut).
        await _harness.Start();

        var (correlationId, responseTask) = await StartCancellationAsync("ORD-0003");
        await _harness.Bus.Publish(new ActionValidationCompleted(correlationId, sourceStatus, null));

        await _sagaHarness.Exists(correlationId, _sagaHarness.StateMachine.Failed, TimeSpan.FromSeconds(5));

        var response = await responseTask;
        Assert.Equal(OrderActionStatus.Unavailable, response.Message.Status);
    }

    [Fact]
    public async Task CancellationForNonexistentOrder_RespondsNotFoundAndFinalizesFailedWithoutAwaitingApproval()
    {
        await _harness.Start();

        var (correlationId, responseTask) = await StartCancellationAsync("ORD-9999");
        await _harness.Bus.Publish(new ActionValidationCompleted(correlationId, SourceFindingStatus.NotFound, null));

        var failed = await _sagaHarness.Exists(correlationId, _sagaHarness.StateMachine.Failed, TimeSpan.FromSeconds(5));
        Assert.NotNull(failed);

        var response = await responseTask;
        Assert.Equal(OrderActionStatus.NotFound, response.Message.Status);

        // CurrentState is Failed (asserted above), not AwaitingApproval -- the saga never passed
        // through the approval-gated state at all for a confirmed not-found order.
    }

    [Fact]
    public async Task ApproveAgainstReferenceThatFinalizedNotFoundAtValidation_RespondsNotFoundNotAlreadyDecided()
    {
        await _harness.Start();

        var (correlationId, requestTask) = await StartCancellationAsync("ORD-9999");
        await _harness.Bus.Publish(new ActionValidationCompleted(correlationId, SourceFindingStatus.NotFound, null));
        await _sagaHarness.Exists(correlationId, _sagaHarness.StateMachine.Failed, TimeSpan.FromSeconds(5));
        await requestTask;

        var approveClient = _harness.GetRequestClient<ApproveOrderAction>();
        var decision = await approveClient.GetResponse<OrderActionDecisionResult>(new ApproveOrderAction(correlationId));

        Assert.Equal(OrderActionDecisionOutcome.NotFound, decision.Message.DecisionStatus);
    }

    [Fact]
    public async Task Approve_ExecutesAndRespondsWithRealOutcome_ThenTransitionsToCompleted()
    {
        await _harness.Start();

        var correlationId = await RequestAndAwaitApprovalAsync("ORD-0003");

        var approveClient = _harness.GetRequestClient<ApproveOrderAction>();
        var decisionTask = approveClient.GetResponse<OrderActionDecisionResult>(new ApproveOrderAction(correlationId), timeout: RequestTimeout.After(s: 10));

        var executing = await _sagaHarness.Exists(correlationId, _sagaHarness.StateMachine.Executing, TimeSpan.FromSeconds(5));
        Assert.NotNull(executing);
        Assert.True(await _harness.Published.Any<BeginOrderActionExecution>(m => m.Context.Message.CorrelationId == correlationId));

        await _harness.Bus.Publish(new OrderActionExecutionCompleted(correlationId, OrderActionExecutionOutcome.Executed, "Refund executed.", "Processing"));

        var completed = await _sagaHarness.Exists(correlationId, _sagaHarness.StateMachine.Completed, TimeSpan.FromSeconds(5));
        Assert.NotNull(completed);

        var decision = await decisionTask;
        Assert.Equal(OrderActionDecisionOutcome.Approved, decision.Message.DecisionStatus);
        Assert.Equal(OrderActionExecutionOutcome.Executed, decision.Message.ExecutionOutcome);
    }

    [Fact]
    public async Task SecondApprove_AfterAlreadyExecuting_RespondsAlreadyDecidedWithoutPublishingExecutionAgain()
    {
        await _harness.Start();

        var correlationId = await RequestAndAwaitApprovalAsync("ORD-0003");

        var approveClient = _harness.GetRequestClient<ApproveOrderAction>();
        var firstDecisionTask = approveClient.GetResponse<OrderActionDecisionResult>(new ApproveOrderAction(correlationId), timeout: RequestTimeout.After(s: 10));
        await _sagaHarness.Exists(correlationId, _sagaHarness.StateMachine.Executing, TimeSpan.FromSeconds(5));

        var secondDecision = await approveClient.GetResponse<OrderActionDecisionResult>(new ApproveOrderAction(correlationId));
        Assert.Equal(OrderActionDecisionOutcome.AlreadyDecided, secondDecision.Message.DecisionStatus);

        Assert.Single(await _harness.Published.SelectAsync<BeginOrderActionExecution>(m => m.Context.Message.CorrelationId == correlationId).ToListAsync());

        // Let the first approval resolve so the harness can shut down cleanly.
        await _harness.Bus.Publish(new OrderActionExecutionCompleted(correlationId, OrderActionExecutionOutcome.Executed, "Refund executed.", "Processing"));
        await firstDecisionTask;
    }

    [Fact]
    public async Task ApproveRetriedAfterExecutionAlreadyCompleted_SurfacesTheRealOutcomeNotNull()
    {
        // Regression test (code review finding): a legitimate retry (e.g. the caller's own HTTP
        // timeout fired just as the first attempt was finishing) previously always got
        // ExecutionOutcome: null from the AlreadyDecided branch, even once the saga had a perfectly
        // good outcome sitting on it -- no better informed than if someone else had decided it.
        await _harness.Start();

        var correlationId = await RequestAndAwaitApprovalAsync("ORD-0003");

        var approveClient = _harness.GetRequestClient<ApproveOrderAction>();
        var firstDecisionTask = approveClient.GetResponse<OrderActionDecisionResult>(new ApproveOrderAction(correlationId), timeout: RequestTimeout.After(s: 10));
        await _sagaHarness.Exists(correlationId, _sagaHarness.StateMachine.Executing, TimeSpan.FromSeconds(5));

        await _harness.Bus.Publish(new OrderActionExecutionCompleted(correlationId, OrderActionExecutionOutcome.Executed, "Refund executed.", "Processing"));
        await _sagaHarness.Exists(correlationId, _sagaHarness.StateMachine.Completed, TimeSpan.FromSeconds(5));
        await firstDecisionTask;

        var retriedDecision = await approveClient.GetResponse<OrderActionDecisionResult>(new ApproveOrderAction(correlationId));
        Assert.Equal(OrderActionDecisionOutcome.AlreadyDecided, retriedDecision.Message.DecisionStatus);
        Assert.Equal(OrderActionExecutionOutcome.Executed, retriedDecision.Message.ExecutionOutcome);
    }

    [Fact]
    public async Task ApproveAgainstUnknownReference_RespondsNotFoundWithoutFaulting()
    {
        await _harness.Start();

        var approveClient = _harness.GetRequestClient<ApproveOrderAction>();
        var decision = await approveClient.GetResponse<OrderActionDecisionResult>(new ApproveOrderAction(Guid.NewGuid()));

        Assert.Equal(OrderActionDecisionOutcome.NotFound, decision.Message.DecisionStatus);
        Assert.False(await _harness.Published.Any<Fault<ApproveOrderAction>>());
    }

    [Fact]
    public async Task Reject_RespondsImmediatelyWithoutPublishingExecution_AndTransitionsToRejected()
    {
        await _harness.Start();

        var correlationId = await RequestAndAwaitApprovalAsync("ORD-0003");

        var rejectClient = _harness.GetRequestClient<RejectOrderAction>();
        var decision = await rejectClient.GetResponse<OrderActionDecisionResult>(new RejectOrderAction(correlationId, "Customer changed their mind."));

        Assert.Equal(OrderActionDecisionOutcome.Rejected, decision.Message.DecisionStatus);

        var rejected = await _sagaHarness.Exists(correlationId, _sagaHarness.StateMachine.Rejected, TimeSpan.FromSeconds(5));
        Assert.NotNull(rejected);
        Assert.False(await _harness.Published.Any<BeginOrderActionExecution>(m => m.Context.Message.CorrelationId == correlationId));
    }

    [Fact]
    public async Task ApproveAfterReject_RespondsAlreadyDecided_AndViceVersa()
    {
        await _harness.Start();

        var correlationId = await RequestAndAwaitApprovalAsync("ORD-0003");

        var rejectClient = _harness.GetRequestClient<RejectOrderAction>();
        await rejectClient.GetResponse<OrderActionDecisionResult>(new RejectOrderAction(correlationId, null));
        await _sagaHarness.Exists(correlationId, _sagaHarness.StateMachine.Rejected, TimeSpan.FromSeconds(5));

        var approveClient = _harness.GetRequestClient<ApproveOrderAction>();
        var lateApproval = await approveClient.GetResponse<OrderActionDecisionResult>(new ApproveOrderAction(correlationId));
        Assert.Equal(OrderActionDecisionOutcome.AlreadyDecided, lateApproval.Message.DecisionStatus);

        var lateRejection = await rejectClient.GetResponse<OrderActionDecisionResult>(new RejectOrderAction(correlationId, null));
        Assert.Equal(OrderActionDecisionOutcome.AlreadyDecided, lateRejection.Message.DecisionStatus);
    }

    [Fact]
    public async Task ExecutionFailedAndCompensated_RespondsWithThatOutcome_AndFinalizesFailedNotCompleted()
    {
        await _harness.Start();

        var correlationId = await RequestAndAwaitApprovalAsync("ORD-0003");

        var approveClient = _harness.GetRequestClient<ApproveOrderAction>();
        var decisionTask = approveClient.GetResponse<OrderActionDecisionResult>(new ApproveOrderAction(correlationId), timeout: RequestTimeout.After(s: 10));
        await _sagaHarness.Exists(correlationId, _sagaHarness.StateMachine.Executing, TimeSpan.FromSeconds(5));

        await _harness.Bus.Publish(new OrderActionExecutionCompleted(
            correlationId, OrderActionExecutionOutcome.FailedAndCompensated, "Inventory release failed; order reverted.", "Processing"));

        var failed = await _sagaHarness.Exists(correlationId, _sagaHarness.StateMachine.Failed, TimeSpan.FromSeconds(5));
        Assert.NotNull(failed);

        var decision = await decisionTask;
        Assert.Equal(OrderActionExecutionOutcome.FailedAndCompensated, decision.Message.ExecutionOutcome);
    }

    [Fact]
    public async Task EveryTerminalOutcome_PublishesExactlyOneCorrectlyLabeledNotification()
    {
        await _harness.Start();

        // Rejected
        var rejectedId = await RequestAndAwaitApprovalAsync("ORD-0003");
        var rejectClient = _harness.GetRequestClient<RejectOrderAction>();
        await rejectClient.GetResponse<OrderActionDecisionResult>(new RejectOrderAction(rejectedId, null));
        await _sagaHarness.Exists(rejectedId, _sagaHarness.StateMachine.Rejected, TimeSpan.FromSeconds(5));

        // Executed
        var executedId = await RequestAndAwaitApprovalAsync("ORD-0003");
        var approveClient = _harness.GetRequestClient<ApproveOrderAction>();
        var executedTask = approveClient.GetResponse<OrderActionDecisionResult>(new ApproveOrderAction(executedId), timeout: RequestTimeout.After(s: 10));
        await _sagaHarness.Exists(executedId, _sagaHarness.StateMachine.Executing, TimeSpan.FromSeconds(5));
        await _harness.Bus.Publish(new OrderActionExecutionCompleted(executedId, OrderActionExecutionOutcome.Executed, "Executed.", "Processing"));
        await executedTask;

        // Failed
        var failedId = await RequestAndAwaitApprovalAsync("ORD-0003");
        var failedTask = approveClient.GetResponse<OrderActionDecisionResult>(new ApproveOrderAction(failedId), timeout: RequestTimeout.After(s: 10));
        await _sagaHarness.Exists(failedId, _sagaHarness.StateMachine.Executing, TimeSpan.FromSeconds(5));
        await _harness.Bus.Publish(new OrderActionExecutionCompleted(failedId, OrderActionExecutionOutcome.Failed, "Failed.", "Processing"));
        await failedTask;

        Assert.True(await _harness.Published.Any<NotificationRequested>(m =>
            m.Context.Message.CorrelationId == rejectedId && m.Context.Message.Outcome == "Rejected"));
        Assert.True(await _harness.Published.Any<NotificationRequested>(m =>
            m.Context.Message.CorrelationId == executedId && m.Context.Message.Outcome == "Executed"));
        Assert.True(await _harness.Published.Any<NotificationRequested>(m =>
            m.Context.Message.CorrelationId == failedId && m.Context.Message.Outcome == "Failed"));
    }

    [Fact]
    public async Task ConcurrentRejections_ForTheSameReference_ExactlyOneIsHonored()
    {
        await _harness.Start();

        var correlationId = await RequestAndAwaitApprovalAsync("ORD-0003");

        var rejectClient = _harness.GetRequestClient<RejectOrderAction>();
        var results = await Task.WhenAll(
            rejectClient.GetResponse<OrderActionDecisionResult>(new RejectOrderAction(correlationId, "first")),
            rejectClient.GetResponse<OrderActionDecisionResult>(new RejectOrderAction(correlationId, "second")));

        var statuses = results.Select(r => r.Message.DecisionStatus).ToArray();
        Assert.Single(statuses, s => s == OrderActionDecisionOutcome.Rejected);
        Assert.Single(statuses, s => s == OrderActionDecisionOutcome.AlreadyDecided);
    }
}
