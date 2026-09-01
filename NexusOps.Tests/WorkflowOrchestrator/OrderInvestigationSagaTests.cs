using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NexusOps.Contracts.Dtos;
using NexusOps.Contracts.Messages;
using OI = NexusOps.WorkflowOrchestrator.OrderInvestigation;

namespace NexusOps.Tests.WorkflowOrchestrator;

/// <summary>
/// Covers spec 005 User Stories 1, 2, and 4: the saga's finalize logic (happy path, degraded,
/// failed, order-not-found), late/orphan findings being discarded (FR-011), and concurrent
/// findings for the same investigation not being lost (FR-009). Uses MassTransit's in-memory test
/// harness -- no broker or Postgres, credential-free per ROADMAP.md's CI constraint. The in-memory
/// saga repository does not itself exercise a real EF Core DbUpdateConcurrencyException/retry --
/// that plumbing (ConcurrencyMode.Optimistic + Postgres xmin) is configured in
/// ServiceCollectionExtensions.cs and is exempt from unit-level coverage per plan.md, which scopes
/// the Aspire.Hosting.Testing integration tier to ROADMAP.md Prompt 6. This suite instead verifies
/// the functional invariant that actually matters: two findings for the same investigation,
/// applied concurrently, are both reflected in the final result.
/// </summary>
public sealed class OrderInvestigationSagaTests : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ITestHarness _harness;
    private readonly ISagaStateMachineTestHarness<OI.OrderInvestigationSaga, OI.OrderInvestigationSagaState> _sagaHarness;

    public OrderInvestigationSagaTests()
    {
        _provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddSagaStateMachine<OI.OrderInvestigationSaga, OI.OrderInvestigationSagaState>()
                    .InMemoryRepository();
            })
            .BuildServiceProvider(true);

        _harness = _provider.GetRequiredService<ITestHarness>();
        _sagaHarness = _harness.GetSagaStateMachineHarness<OI.OrderInvestigationSaga, OI.OrderInvestigationSagaState>();
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

    private static readonly InventoryLevel[] SampleLevels =
    [
        new("SKU-ELEC-001", "Wireless Headphones Pro", "WH-EAST-01", 0, 10, DateTime.UtcNow)
    ];

    private static readonly ProductDetail[] SampleProducts =
    [
        new("PRD-0001", "SKU-ELEC-001", "Wireless Headphones Pro", "desc", "Electronics", 299.99m, 0.28m)
    ];

    private async Task<(Guid CorrelationId, Task<Response<RootCauseInvestigationResult>> ResponseTask)> StartInvestigationAsync(string orderId)
    {
        var client = _harness.GetRequestClient<InvestigateOrderRootCause>();
        var task = client.GetResponse<RootCauseInvestigationResult>(new InvestigateOrderRootCause(orderId), timeout: RequestTimeout.After(s: 10));

        var ids = await _sagaHarness.Exists(x => x.OrderId == orderId, _sagaHarness.StateMachine.Investigating, TimeSpan.FromSeconds(5));
        Assert.NotNull(ids);
        Assert.NotEmpty(ids);

        return (ids[0], task);
    }

    [Fact]
    public async Task AllSourcesSucceed_FinalizesCompleteAndRespondsToTheCaller()
    {
        await _harness.Start();

        var (correlationId, responseTask) = await StartInvestigationAsync("ORD-0003");

        await _harness.Bus.Publish(new OrderFindingReported(correlationId, SourceFindingStatus.Succeeded, SampleOrder));
        await _harness.Bus.Publish(new InventoryFindingReported(correlationId, SourceFindingStatus.Succeeded, SampleLevels, []));
        await _harness.Bus.Publish(new ProductFindingReported(correlationId, SourceFindingStatus.Succeeded, SampleProducts, []));

        var completedId = await _sagaHarness.Exists(correlationId, _sagaHarness.StateMachine.Completed, TimeSpan.FromSeconds(5));
        Assert.NotNull(completedId);

        var response = await responseTask;
        Assert.Equal(InvestigationCompleteness.Complete, response.Message.Completeness);
        Assert.Empty(response.Message.DegradedSources);
        Assert.NotNull(response.Message.Order);
    }

    [Fact]
    public async Task OneSourceUnavailable_FinalizesDegradedWithTheCorrectSourceNamed()
    {
        await _harness.Start();

        var (correlationId, responseTask) = await StartInvestigationAsync("ORD-0003");

        await _harness.Bus.Publish(new OrderFindingReported(correlationId, SourceFindingStatus.Succeeded, SampleOrder));
        await _harness.Bus.Publish(new InventoryFindingReported(correlationId, SourceFindingStatus.Unavailable, [], []));
        await _harness.Bus.Publish(new ProductFindingReported(correlationId, SourceFindingStatus.Succeeded, SampleProducts, []));

        var completedId = await _sagaHarness.Exists(correlationId, _sagaHarness.StateMachine.Completed, TimeSpan.FromSeconds(5));
        Assert.NotNull(completedId);

        var response = await responseTask;
        Assert.Equal(InvestigationCompleteness.Degraded, response.Message.Completeness);
        Assert.Contains("Inventory", response.Message.DegradedSources);
        Assert.DoesNotContain("Product", response.Message.DegradedSources);
    }

    [Fact]
    public async Task OrderSourceUnavailable_FinalizesFailedRegardlessOfOtherSources()
    {
        await _harness.Start();

        var (correlationId, responseTask) = await StartInvestigationAsync("ORD-0003");

        await _harness.Bus.Publish(new OrderFindingReported(correlationId, SourceFindingStatus.TimedOut, null));
        await _harness.Bus.Publish(new InventoryFindingReported(correlationId, SourceFindingStatus.Unavailable, [], []));
        await _harness.Bus.Publish(new ProductFindingReported(correlationId, SourceFindingStatus.Unavailable, [], []));

        var failedId = await _sagaHarness.Exists(correlationId, _sagaHarness.StateMachine.Failed, TimeSpan.FromSeconds(5));
        Assert.NotNull(failedId);

        var response = await responseTask;
        Assert.Equal(InvestigationCompleteness.Failed, response.Message.Completeness);
    }

    [Fact]
    public async Task OrderNotFound_WithNothingToCheck_FinalizesCompleteNotFailed()
    {
        await _harness.Start();

        var (correlationId, responseTask) = await StartInvestigationAsync("ORD-9999");

        // A confirmed not-found is a completed finding, not a degraded/failed source
        // (spec.md Edge Cases) -- distinct from OrderSourceUnavailable above.
        await _harness.Bus.Publish(new OrderFindingReported(correlationId, SourceFindingStatus.NotFound, null));
        await _harness.Bus.Publish(new InventoryFindingReported(correlationId, SourceFindingStatus.NotFound, [], []));
        await _harness.Bus.Publish(new ProductFindingReported(correlationId, SourceFindingStatus.NotFound, [], []));

        var completedId = await _sagaHarness.Exists(correlationId, _sagaHarness.StateMachine.Completed, TimeSpan.FromSeconds(5));
        Assert.NotNull(completedId);

        var response = await responseTask;
        Assert.Equal(InvestigationCompleteness.Complete, response.Message.Completeness);
    }

    [Fact]
    public async Task FindingForUnknownCorrelationId_IsDiscardedWithoutFaulting()
    {
        await _harness.Start();

        var orphanId = Guid.NewGuid();
        await _harness.Bus.Publish(new OrderFindingReported(orphanId, SourceFindingStatus.Succeeded, SampleOrder));

        Assert.True(await _harness.Consumed.Any<OrderFindingReported>());
        Assert.False(await _harness.Published.Any<Fault<OrderFindingReported>>());

        var neverExisted = await _sagaHarness.Exists(orphanId, _sagaHarness.StateMachine.Investigating, TimeSpan.FromSeconds(1));
        Assert.Null(neverExisted);
    }

    [Fact]
    public async Task ConcurrentFindings_ForTheSameInvestigation_NeitherUpdateIsLost()
    {
        await _harness.Start();

        var (correlationId, responseTask) = await StartInvestigationAsync("ORD-0003");

        // Two findings published concurrently for the same saga instance -- a genuine race on the
        // same persisted row in the real EF Core/Postgres repository (FR-009). If either update
        // were silently lost, the saga would never see all three findings and Completeness would
        // never resolve to Complete.
        await Task.WhenAll(
            _harness.Bus.Publish(new InventoryFindingReported(correlationId, SourceFindingStatus.Succeeded, SampleLevels, [])),
            _harness.Bus.Publish(new ProductFindingReported(correlationId, SourceFindingStatus.Succeeded, SampleProducts, [])));
        await _harness.Bus.Publish(new OrderFindingReported(correlationId, SourceFindingStatus.Succeeded, SampleOrder));

        var completedId = await _sagaHarness.Exists(correlationId, _sagaHarness.StateMachine.Completed, TimeSpan.FromSeconds(5));
        Assert.NotNull(completedId);

        var response = await responseTask;
        Assert.Equal(InvestigationCompleteness.Complete, response.Message.Completeness);
    }
}
