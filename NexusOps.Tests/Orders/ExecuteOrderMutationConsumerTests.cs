using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NexusOps.Contracts.Dtos;
using NexusOps.Contracts.Messages;
using NexusOps.OrderService.Consumers;
using NexusOps.OrderService.Data;

namespace NexusOps.Tests.Orders;

/// <summary>
/// Covers spec 006 FR-001/FR-013: a refund amount must actually be validated and applied, not
/// merely quoted back to the caller (code review finding — Amount was previously plumbed through
/// the tool, saga, and message, then never read by the consumer that executes the mutation).
/// </summary>
public sealed class ExecuteOrderMutationConsumerTests
{
    private static async Task<(ITestHarness Harness, OrderMutationOverlay Overlay)> StartHarnessAsync()
    {
        var overlay = new OrderMutationOverlay();
        var provider = new ServiceCollection()
            .AddSingleton(overlay)
            .AddSingleton(FixedTimeProvider.Default as TimeProvider)
            .AddMassTransitTestHarness(x => x.AddConsumer<ExecuteOrderMutationConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return (harness, overlay);
    }

    [Fact]
    public async Task PartialRefund_WithinOrderTotal_IsAppliedToTheOverlay()
    {
        var (harness, overlay) = await StartHarnessAsync();

        var client = harness.GetRequestClient<ExecuteOrderMutation>();
        var response = await client.GetResponse<OrderMutationExecuted>(
            new ExecuteOrderMutation(Guid.NewGuid(), OrderActionType.Refund, "ORD-0003", 50m));

        Assert.True(response.Message.Success);
        Assert.True(overlay.TryGet("ORD-0003", out var @override));
        Assert.Equal(50m, @override.RefundedAmount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(999999)]
    public async Task RefundAmount_OutsideValidRange_IsRejectedAndOverlayUntouched(decimal amount)
    {
        var (harness, overlay) = await StartHarnessAsync();

        var client = harness.GetRequestClient<ExecuteOrderMutation>();
        var response = await client.GetResponse<OrderMutationExecuted>(
            new ExecuteOrderMutation(Guid.NewGuid(), OrderActionType.Refund, "ORD-0003", amount));

        Assert.False(response.Message.Success);
        Assert.NotNull(response.Message.FailureReason);
        Assert.False(overlay.TryGet("ORD-0003", out _));
    }

    [Fact]
    public async Task Cancellation_DoesNotTouchRefundedAmount()
    {
        var (harness, overlay) = await StartHarnessAsync();

        var client = harness.GetRequestClient<ExecuteOrderMutation>();
        var response = await client.GetResponse<OrderMutationExecuted>(
            new ExecuteOrderMutation(Guid.NewGuid(), OrderActionType.Cancellation, "ORD-0004", null));

        Assert.True(response.Message.Success);
        Assert.True(overlay.TryGet("ORD-0004", out var @override));
        Assert.Null(@override.RefundedAmount);
    }
}
