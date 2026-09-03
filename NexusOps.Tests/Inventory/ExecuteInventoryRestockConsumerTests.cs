using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NexusOps.Contracts.Messages;
using NexusOps.InventoryService.Consumers;
using NexusOps.InventoryService.Data;

namespace NexusOps.Tests.Inventory;

/// <summary>
/// Covers spec 006 User Story 7 / FR-019: a redelivered <c>ExecuteInventoryRestock</c> (e.g.
/// <c>OrderActionExecutionConsumer</c> crashing after this call succeeds but before publishing
/// <c>OrderActionExecutionCompleted</c>) must not double-credit the same restock — unlike the order
/// mutation, there is no natural "already restocked" guard, so this consumer needs its own
/// idempotency check (code review finding).
/// </summary>
public sealed class ExecuteInventoryRestockConsumerTests
{
    private static async Task<(ITestHarness Harness, InventoryMutationOverlay Overlay)> StartHarnessAsync()
    {
        var overlay = new InventoryMutationOverlay();
        var provider = new ServiceCollection()
            .AddSingleton(overlay)
            .AddMassTransitTestHarness(x => x.AddConsumer<ExecuteInventoryRestockConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return (harness, overlay);
    }

    [Fact]
    public async Task Restock_IsAppliedToTheOverlay()
    {
        var (harness, overlay) = await StartHarnessAsync();

        var client = harness.GetRequestClient<ExecuteInventoryRestock>();
        var response = await client.GetResponse<InventoryRestockExecuted>(
            new ExecuteInventoryRestock(Guid.NewGuid(), "ORD-0003", [new InventoryRestockLine("SKU-ELEC-001", 1)]));

        Assert.True(response.Message.Success);
        Assert.Equal(1, overlay.GetDelta("SKU-ELEC-001"));
    }

    [Fact]
    public async Task RedeliveredRestock_ForTheSameCorrelationId_IsAppliedOnlyOnce()
    {
        var (harness, overlay) = await StartHarnessAsync();

        var correlationId = Guid.NewGuid();
        var client = harness.GetRequestClient<ExecuteInventoryRestock>();
        var lines = new[] { new InventoryRestockLine("SKU-ELEC-001", 1) };

        await client.GetResponse<InventoryRestockExecuted>(new ExecuteInventoryRestock(correlationId, "ORD-0003", lines));
        await client.GetResponse<InventoryRestockExecuted>(new ExecuteInventoryRestock(correlationId, "ORD-0003", lines));

        Assert.Equal(1, overlay.GetDelta("SKU-ELEC-001"));
    }

    [Fact]
    public async Task RestockWithADifferentCorrelationId_IsAppliedIndependently()
    {
        var (harness, overlay) = await StartHarnessAsync();

        var client = harness.GetRequestClient<ExecuteInventoryRestock>();
        var lines = new[] { new InventoryRestockLine("SKU-ELEC-001", 1) };

        await client.GetResponse<InventoryRestockExecuted>(new ExecuteInventoryRestock(Guid.NewGuid(), "ORD-0003", lines));
        await client.GetResponse<InventoryRestockExecuted>(new ExecuteInventoryRestock(Guid.NewGuid(), "ORD-0004", lines));

        Assert.Equal(2, overlay.GetDelta("SKU-ELEC-001"));
    }
}
