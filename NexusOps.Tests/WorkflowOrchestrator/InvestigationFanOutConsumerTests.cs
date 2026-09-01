using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NexusOps.Contracts.Dtos;
using NexusOps.Contracts.Messages;
using OI = NexusOps.WorkflowOrchestrator.OrderInvestigation;

namespace NexusOps.Tests.WorkflowOrchestrator;

/// <summary>
/// Covers spec 005 User Story 2 at the fan-out coordinator level: the order lookup runs first,
/// inventory+product run concurrently once it succeeds, and every possible outcome of each leg
/// (success, fault, no responder at all -> timeout) is turned into a published finding rather than
/// an unhandled exception. Each test wires the real <see cref="OI.InvestigationFanOutConsumer"/>
/// against small stand-in consumers for the three domain services, using MassTransit's in-memory
/// test harness.
/// </summary>
public sealed class InvestigationFanOutConsumerTests
{
    private static readonly OrderSummary SampleOrder = new(
        OrderId: "ORD-0003",
        CustomerId: "CUST-003",
        Status: "processing",
        TotalAmount: 299.99m,
        ExpectedDelivery: DateOnly.FromDateTime(DateTime.UtcNow),
        ActualDelivery: null,
        LineItems: [new OrderLineItem("SKU-ELEC-001", "Wireless Headphones Pro", 1, 299.99m)]);

    private sealed class RespondingOrderConsumer : IConsumer<RequestOrderFinding>
    {
        public Task Consume(ConsumeContext<RequestOrderFinding> context) =>
            context.RespondAsync(new OrderFindingReported(context.Message.CorrelationId, SourceFindingStatus.Succeeded, SampleOrder));
    }

    private sealed class FaultingOrderConsumer : IConsumer<RequestOrderFinding>
    {
        public Task Consume(ConsumeContext<RequestOrderFinding> context) =>
            throw new InvalidOperationException("Order service is having a bad day.");
    }

    private sealed class NotFoundOrderConsumer : IConsumer<RequestOrderFinding>
    {
        public Task Consume(ConsumeContext<RequestOrderFinding> context) =>
            context.RespondAsync(new OrderFindingReported(context.Message.CorrelationId, SourceFindingStatus.NotFound, null));
    }

    private sealed class RespondingInventoryConsumer : IConsumer<RequestInventoryFinding>
    {
        public Task Consume(ConsumeContext<RequestInventoryFinding> context) =>
            context.RespondAsync(new InventoryFindingReported(
                context.Message.CorrelationId,
                SourceFindingStatus.Succeeded,
                [new InventoryLevel("SKU-ELEC-001", "Wireless Headphones Pro", "WH-EAST-01", 0, 10, DateTime.UtcNow)],
                []));
    }

    private sealed class FaultingInventoryConsumer : IConsumer<RequestInventoryFinding>
    {
        public Task Consume(ConsumeContext<RequestInventoryFinding> context) =>
            throw new InvalidOperationException("Inventory service is having a bad day.");
    }

    private sealed class RespondingProductConsumer : IConsumer<RequestProductFinding>
    {
        public Task Consume(ConsumeContext<RequestProductFinding> context) =>
            context.RespondAsync(new ProductFindingReported(
                context.Message.CorrelationId,
                SourceFindingStatus.Succeeded,
                [new ProductDetail("PRD-0001", "SKU-ELEC-001", "Wireless Headphones Pro", "desc", "Electronics", 299.99m, 0.28m)],
                []));
    }

    [Fact]
    public async Task AllThreeSourcesHealthy_PublishesThreeSucceededFindings()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<OI.InvestigationFanOutConsumer>();
                x.AddConsumer<RespondingOrderConsumer>();
                x.AddConsumer<RespondingInventoryConsumer>();
                x.AddConsumer<RespondingProductConsumer>();
                x.AddRequestClient<RequestOrderFinding>();
                x.AddRequestClient<RequestInventoryFinding>();
                x.AddRequestClient<RequestProductFinding>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var correlationId = Guid.NewGuid();
        await harness.Bus.Publish(new NexusOps.Contracts.Messages.BeginInvestigationFanOut(correlationId, "ORD-0003"));

        Assert.True(await harness.Published.Any<OrderFindingReported>(m => m.Context.Message.Status == SourceFindingStatus.Succeeded));
        Assert.True(await harness.Published.Any<InventoryFindingReported>(m => m.Context.Message.Status == SourceFindingStatus.Succeeded));
        Assert.True(await harness.Published.Any<ProductFindingReported>(m => m.Context.Message.Status == SourceFindingStatus.Succeeded));
    }

    [Fact]
    public async Task OrderServiceFaults_PublishesUnavailableForAllThreeSources()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<OI.InvestigationFanOutConsumer>();
                x.AddConsumer<FaultingOrderConsumer>();
                x.AddRequestClient<RequestOrderFinding>();
                x.AddRequestClient<RequestInventoryFinding>();
                x.AddRequestClient<RequestProductFinding>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var correlationId = Guid.NewGuid();
        await harness.Bus.Publish(new NexusOps.Contracts.Messages.BeginInvestigationFanOut(correlationId, "ORD-0003"));

        Assert.True(await harness.Published.Any<OrderFindingReported>(m => m.Context.Message.Status == SourceFindingStatus.Unavailable));
        // No SKUs to look up once the order itself couldn't be identified -- both remaining
        // sources are reported immediately rather than left pending.
        Assert.True(await harness.Published.Any<InventoryFindingReported>(m => m.Context.Message.Status == SourceFindingStatus.Unavailable));
        Assert.True(await harness.Published.Any<ProductFindingReported>(m => m.Context.Message.Status == SourceFindingStatus.Unavailable));
    }

    [Fact]
    public async Task OrderConfirmedNotFound_PublishesNotFoundNotUnavailableForAllThreeSources()
    {
        // Regression test: a confirmed not-found order was previously mislabeled as an
        // "Unavailable" source for Inventory/Product, which the saga's ComputeCompleteness then
        // treated as a degraded source instead of the "nothing to check" completed case spec.md's
        // Edge Cases actually calls for.
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<OI.InvestigationFanOutConsumer>();
                x.AddConsumer<NotFoundOrderConsumer>();
                x.AddRequestClient<RequestOrderFinding>();
                x.AddRequestClient<RequestInventoryFinding>();
                x.AddRequestClient<RequestProductFinding>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var correlationId = Guid.NewGuid();
        await harness.Bus.Publish(new NexusOps.Contracts.Messages.BeginInvestigationFanOut(correlationId, "ORD-9999"));

        Assert.True(await harness.Published.Any<OrderFindingReported>(m => m.Context.Message.Status == SourceFindingStatus.NotFound));
        Assert.True(await harness.Published.Any<InventoryFindingReported>(m => m.Context.Message.Status == SourceFindingStatus.NotFound));
        Assert.True(await harness.Published.Any<ProductFindingReported>(m => m.Context.Message.Status == SourceFindingStatus.NotFound));
    }

    [Fact]
    public async Task OrderServiceNeverResponds_PublishesTimedOut()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<OI.InvestigationFanOutConsumer>();
                // Deliberately no consumer registered for RequestOrderFinding -- the request
                // client's own per-source timeout (5s) must still resolve the call.
                x.AddRequestClient<RequestOrderFinding>();
                x.AddRequestClient<RequestInventoryFinding>();
                x.AddRequestClient<RequestProductFinding>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        harness.TestTimeout = TimeSpan.FromSeconds(15);
        await harness.Start();

        var correlationId = Guid.NewGuid();
        await harness.Bus.Publish(new NexusOps.Contracts.Messages.BeginInvestigationFanOut(correlationId, "ORD-0003"));

        Assert.True(await harness.Published.Any<OrderFindingReported>(m => m.Context.Message.Status == SourceFindingStatus.TimedOut));
    }

    [Fact]
    public async Task InventoryServiceFaults_ButOrderAndProductSucceed_OnlyInventoryIsUnavailable()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<OI.InvestigationFanOutConsumer>();
                x.AddConsumer<RespondingOrderConsumer>();
                x.AddConsumer<FaultingInventoryConsumer>();
                x.AddConsumer<RespondingProductConsumer>();
                x.AddRequestClient<RequestOrderFinding>();
                x.AddRequestClient<RequestInventoryFinding>();
                x.AddRequestClient<RequestProductFinding>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var correlationId = Guid.NewGuid();
        await harness.Bus.Publish(new NexusOps.Contracts.Messages.BeginInvestigationFanOut(correlationId, "ORD-0003"));

        Assert.True(await harness.Published.Any<OrderFindingReported>(m => m.Context.Message.Status == SourceFindingStatus.Succeeded));
        Assert.True(await harness.Published.Any<InventoryFindingReported>(m => m.Context.Message.Status == SourceFindingStatus.Unavailable));
        Assert.True(await harness.Published.Any<ProductFindingReported>(m => m.Context.Message.Status == SourceFindingStatus.Succeeded));
    }
}
