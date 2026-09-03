using System.Net.Http.Json;
using MassTransit;
using NexusOps.Contracts.Dtos;
using NexusOps.Contracts.Messages;

namespace NexusOps.IntegrationTests;

/// <summary>
/// End-to-end coverage for both sagas against the real message bus and real infrastructure
/// (ROADMAP.md Prompt 6, deferred by both specs/005-workflow-orchestrator/plan.md and
/// specs/006-approval-actions/plan.md). <see cref="WorkflowOrchestratorFixture"/> is shared across
/// every test in this class (xUnit runs test methods within one class sequentially by default, so
/// the resource stop/start in <see cref="InvestigationSaga_ReturnsPartialResults_WhenInventoryServiceIsStopped"/>
/// cannot race another test's assertions), and each test uses its own seed order ID so no test
/// depends on another's mutation.
/// </summary>
public sealed class WorkflowOrchestratorIntegrationTests(WorkflowOrchestratorFixture fixture)
    : IClassFixture<WorkflowOrchestratorFixture>
{
    [Fact]
    public async Task InvestigationSaga_HappyPath_ReturnsAggregatedResults()
    {
        // ORD-0003 references SKU-ELEC-001, seeded with zero stock on hand -- OrderStore.cs's own
        // "cross-service integrity" case. All three sources answer, so this is a Complete result
        // that still surfaces the stockout, not a degraded one.
        var response = await fixture.RootCauseClient.GetResponse<RootCauseInvestigationResult>(
            new InvestigateOrderRootCause("ORD-0003"));

        var result = response.Message;

        Assert.Equal(InvestigationCompleteness.Complete, result.Completeness);
        Assert.Empty(result.DegradedSources);

        Assert.Equal(SourceFindingStatus.Succeeded, result.OrderFinding);
        Assert.NotNull(result.Order);
        Assert.Equal("ORD-0003", result.Order!.OrderId);

        Assert.Equal(SourceFindingStatus.Succeeded, result.InventoryFinding);
        var inventoryLevel = Assert.Single(result.InventoryLevels, l => l.Sku == "SKU-ELEC-001");
        Assert.Equal(0, inventoryLevel.QuantityOnHand);

        Assert.Equal(SourceFindingStatus.Succeeded, result.ProductFinding);
        Assert.Contains(result.ProductDetails, p => p.Sku == "SKU-ELEC-001");
    }

    [Fact]
    public async Task InvestigationSaga_ReturnsPartialResults_WhenInventoryServiceIsStopped()
    {
        // The stop call itself is inside the try -- a timeout in *its* terminal-state wait must
        // still trigger the finally's restart attempt, or a flaky stop leaves inventory-service down
        // for every later test sharing this fixture (previously: the stop sat outside the try, so
        // exactly that failure mode skipped the restart entirely).
        try
        {
            await fixture.StopResourceAsync("inventory-service");

            // ORD-0001's own line item (SKU-ELEC-002) plays no role here -- the point of this case
            // is that the inventory *source* is unreachable, not any particular SKU's data.
            var response = await fixture.RootCauseClient.GetResponse<RootCauseInvestigationResult>(
                new InvestigateOrderRootCause("ORD-0001"));

            var result = response.Message;

            Assert.Equal(InvestigationCompleteness.Degraded, result.Completeness);
            Assert.NotEmpty(result.DegradedSources);

            Assert.Equal(SourceFindingStatus.Succeeded, result.OrderFinding);
            Assert.NotNull(result.Order);

            Assert.True(
                result.InventoryFinding is SourceFindingStatus.Unavailable or SourceFindingStatus.TimedOut,
                $"Expected the inventory finding to reflect the stopped service, but got {result.InventoryFinding}.");
        }
        finally
        {
            // Restart unconditionally so later tests (order not guaranteed by xUnit) never depend
            // on this test having run, or having run first.
            await fixture.StartResourceAsync("inventory-service");
        }
    }

    [Fact]
    public async Task ActionSaga_BlocksUntilApproval_ThenExecutesOnApprove()
    {
        const string orderId = "ORD-0004";
        const decimal refundAmount = 40.00m;

        var requestResponse = await fixture.RefundClient.GetResponse<OrderActionRequestResult>(
            new RequestOrderRefund(orderId, refundAmount, "integration test: approve path"));

        var request = requestResponse.Message;
        Assert.Equal(OrderActionStatus.AwaitingApproval, request.Status);
        Assert.NotEqual(Guid.Empty, request.ApprovalReference);

        // Nothing has executed yet -- the whole point of the approval gate (Constitution
        // Principle III). Confirmed directly against the order service, not just inferred from the
        // saga's own reported status.
        using (var orderServiceClient = fixture.CreateOrderServiceClient())
        {
            var beforeApproval = await orderServiceClient.GetFromJsonAsync<OrderSummary>($"/orders/{orderId}");
            Assert.NotNull(beforeApproval);
            Assert.Null(beforeApproval!.RefundedAmount);
            Assert.NotEqual("refunded", beforeApproval.Status);
        }

        var decisionResponse = await fixture.ApproveClient.GetResponse<OrderActionDecisionResult>(
            new ApproveOrderAction(request.ApprovalReference));

        var decision = decisionResponse.Message;
        Assert.Equal(OrderActionDecisionOutcome.Approved, decision.DecisionStatus);
        Assert.Equal(OrderActionExecutionOutcome.Executed, decision.ExecutionOutcome);

        using (var orderServiceClient = fixture.CreateOrderServiceClient())
        {
            var afterApproval = await orderServiceClient.GetFromJsonAsync<OrderSummary>($"/orders/{orderId}");
            Assert.NotNull(afterApproval);
            Assert.Equal("refunded", afterApproval!.Status);
            Assert.Equal(refundAmount, afterApproval.RefundedAmount);
        }
    }

    [Fact]
    public async Task ActionSaga_RejectsCleanly_LeavingTheOrderUntouched()
    {
        const string orderId = "ORD-0007";

        var requestResponse = await fixture.CancellationClient.GetResponse<OrderActionRequestResult>(
            new RequestOrderCancellation(orderId, "integration test: reject path"));

        var request = requestResponse.Message;
        Assert.Equal(OrderActionStatus.AwaitingApproval, request.Status);
        Assert.NotEqual(Guid.Empty, request.ApprovalReference);

        var decisionResponse = await fixture.RejectClient.GetResponse<OrderActionDecisionResult>(
            new RejectOrderAction(request.ApprovalReference, "integration test: not needed"));

        var decision = decisionResponse.Message;
        Assert.Equal(OrderActionDecisionOutcome.Rejected, decision.DecisionStatus);
        Assert.Null(decision.ExecutionOutcome);

        using var orderServiceClient = fixture.CreateOrderServiceClient();
        var afterReject = await orderServiceClient.GetFromJsonAsync<OrderSummary>($"/orders/{orderId}");
        Assert.NotNull(afterReject);
        Assert.Equal("delivered", afterReject!.Status);
        Assert.Null(afterReject.RefundedAmount);

        // A second decision against an already-rejected reference must be reported, not silently
        // re-applied or ignored (spec.md's AlreadyDecided outcome).
        var secondDecision = await fixture.RejectClient.GetResponse<OrderActionDecisionResult>(
            new RejectOrderAction(request.ApprovalReference, "second attempt"));
        Assert.Equal(OrderActionDecisionOutcome.AlreadyDecided, secondDecision.Message.DecisionStatus);
    }
}
