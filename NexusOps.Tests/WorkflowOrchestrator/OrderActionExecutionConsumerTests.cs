using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NexusOps.Contracts.Dtos;
using NexusOps.Contracts.Messages;
using OA = NexusOps.WorkflowOrchestrator.OrderAction;

namespace NexusOps.Tests.WorkflowOrchestrator;

/// <summary>
/// Covers spec 006 User Story 4 at the execution-consumer level: a refund's single dependency, a
/// cancellation's two dependencies (order then inventory), and the compensation trigger when the
/// second dependency fails after the first already succeeded. Each test wires the real
/// <see cref="OA.OrderActionExecutionConsumer"/> against small stand-in consumers for the two
/// domain services, using MassTransit's in-memory test harness — mirrors
/// <c>InvestigationFanOutConsumerTests.cs</c>'s (feature 005) shape exactly.
/// </summary>
public sealed class OrderActionExecutionConsumerTests
{
    private static readonly OrderLineItem[] SampleLineItems = [new("SKU-ELEC-001", "Wireless Headphones Pro", 1, 299.99m)];

    private sealed class SucceedingOrderMutationConsumer : IConsumer<ExecuteOrderMutation>
    {
        public Task Consume(ConsumeContext<ExecuteOrderMutation> context) =>
            context.RespondAsync(new OrderMutationExecuted(context.Message.CorrelationId, true, null, "Processing", SampleLineItems));
    }

    private sealed class FailingOrderMutationConsumer : IConsumer<ExecuteOrderMutation>
    {
        public Task Consume(ConsumeContext<ExecuteOrderMutation> context) =>
            context.RespondAsync(new OrderMutationExecuted(context.Message.CorrelationId, false, "Order is already refunded.", "Refunded", []));
    }

    private sealed class SucceedingInventoryRestockConsumer : IConsumer<ExecuteInventoryRestock>
    {
        public Task Consume(ConsumeContext<ExecuteInventoryRestock> context) =>
            context.RespondAsync(new InventoryRestockExecuted(context.Message.CorrelationId, true, null));
    }

    private sealed class FailingInventoryRestockConsumer : IConsumer<ExecuteInventoryRestock>
    {
        public Task Consume(ConsumeContext<ExecuteInventoryRestock> context) =>
            context.RespondAsync(new InventoryRestockExecuted(context.Message.CorrelationId, false, "Inventory service is having a bad day."));
    }

    private sealed class SucceedingCompensateConsumer : IConsumer<CompensateOrderMutation>
    {
        public Task Consume(ConsumeContext<CompensateOrderMutation> context) =>
            context.RespondAsync(new OrderMutationCompensated(context.Message.CorrelationId, true));
    }

    [Fact]
    public async Task Refund_OrderMutationSucceeds_PublishesExecuted()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<OA.OrderActionExecutionConsumer>();
                x.AddConsumer<SucceedingOrderMutationConsumer>();
                x.AddRequestClient<ExecuteOrderMutation>();
                x.AddRequestClient<ExecuteInventoryRestock>();
                x.AddRequestClient<CompensateOrderMutation>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var correlationId = Guid.NewGuid();
        await harness.Bus.Publish(new BeginOrderActionExecution(correlationId, OrderActionType.Refund, "ORD-0003", 299.99m));

        Assert.True(await harness.Published.Any<OrderActionExecutionCompleted>(m => m.Context.Message.Outcome == OrderActionExecutionOutcome.Executed));
        Assert.False(await harness.Consumed.Any<ExecuteInventoryRestock>());
    }

    [Fact]
    public async Task Cancellation_BothDependenciesSucceed_PublishesExecutedWithNoCompensation()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<OA.OrderActionExecutionConsumer>();
                x.AddConsumer<SucceedingOrderMutationConsumer>();
                x.AddConsumer<SucceedingInventoryRestockConsumer>();
                x.AddRequestClient<ExecuteOrderMutation>();
                x.AddRequestClient<ExecuteInventoryRestock>();
                x.AddRequestClient<CompensateOrderMutation>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var correlationId = Guid.NewGuid();
        await harness.Bus.Publish(new BeginOrderActionExecution(correlationId, OrderActionType.Cancellation, "ORD-0003", null));

        Assert.True(await harness.Published.Any<OrderActionExecutionCompleted>(m => m.Context.Message.Outcome == OrderActionExecutionOutcome.Executed));
        Assert.False(await harness.Consumed.Any<CompensateOrderMutation>());
    }

    [Fact]
    public async Task Cancellation_InventoryFailsAfterOrderSucceeds_CompensatesAndPublishesFailedAndCompensated()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<OA.OrderActionExecutionConsumer>();
                x.AddConsumer<SucceedingOrderMutationConsumer>();
                x.AddConsumer<FailingInventoryRestockConsumer>();
                x.AddConsumer<SucceedingCompensateConsumer>();
                x.AddRequestClient<ExecuteOrderMutation>();
                x.AddRequestClient<ExecuteInventoryRestock>();
                x.AddRequestClient<CompensateOrderMutation>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var correlationId = Guid.NewGuid();
        await harness.Bus.Publish(new BeginOrderActionExecution(correlationId, OrderActionType.Cancellation, "ORD-0003", null));

        Assert.True(await harness.Consumed.Any<CompensateOrderMutation>(m => m.Context.Message.RevertToStatus == "Processing"));
        Assert.True(await harness.Published.Any<OrderActionExecutionCompleted>(m => m.Context.Message.Outcome == OrderActionExecutionOutcome.FailedAndCompensated));
    }

    [Fact]
    public async Task Cancellation_OrderMutationItselfFails_PublishesFailedWithoutAttemptingInventoryOrCompensation()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<OA.OrderActionExecutionConsumer>();
                x.AddConsumer<FailingOrderMutationConsumer>();
                x.AddRequestClient<ExecuteOrderMutation>();
                x.AddRequestClient<ExecuteInventoryRestock>();
                x.AddRequestClient<CompensateOrderMutation>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var correlationId = Guid.NewGuid();
        await harness.Bus.Publish(new BeginOrderActionExecution(correlationId, OrderActionType.Cancellation, "ORD-0003", null));

        Assert.True(await harness.Published.Any<OrderActionExecutionCompleted>(m => m.Context.Message.Outcome == OrderActionExecutionOutcome.Failed));
        Assert.False(await harness.Consumed.Any<ExecuteInventoryRestock>());
        Assert.False(await harness.Consumed.Any<CompensateOrderMutation>());
    }
}
