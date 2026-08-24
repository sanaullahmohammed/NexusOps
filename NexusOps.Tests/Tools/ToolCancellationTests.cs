using System.Net;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using NexusOps.AgentHost.Tools;

namespace NexusOps.Tests.Tools;

/// <summary>
/// Covers 003 FR-014: every tool handler accepts and forwards a cancellation token, and a
/// cancellation is never reclassified as a service outage.
/// </summary>
public class ToolCancellationTests
{
    /// <summary>Records the token it was handed, then blocks until that token is cancelled.</summary>
    private sealed class BlockingHandler : HttpMessageHandler
    {
        public CancellationToken ObservedToken { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ObservedToken = cancellationToken;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://stub") };
    }

    private static (OrderTools Order, InventoryTools Inventory, ProductTools Product, BlockingHandler Handler) Build()
    {
        var handler = new BlockingHandler();
        var factory = new StubFactory(handler);
        return (
            new OrderTools(factory, NullLogger<OrderTools>.Instance),
            new InventoryTools(factory, NullLogger<InventoryTools>.Instance),
            new ProductTools(factory, NullLogger<ProductTools>.Instance),
            handler);
    }

    public static TheoryData<string> AllToolMethods => new()
    {
        nameof(OrderTools.InvestigateOrderAnomalyAsync),
        nameof(OrderTools.GetOrderDetailsAsync),
        nameof(InventoryTools.GetInventoryAlertsAsync),
        nameof(InventoryTools.GetInventoryLevelAsync),
        nameof(ProductTools.GetProductDetailsAsync),
        nameof(ProductTools.ListProductsByCategoryAsync)
    };

    [Theory]
    [MemberData(nameof(AllToolMethods))]
    public void EveryToolMethod_AcceptsATrailingCancellationToken(string methodName)
    {
        var method = new[] { typeof(OrderTools), typeof(InventoryTools), typeof(ProductTools) }
            .Select(t => t.GetMethod(methodName))
            .Single(m => m is not null)!;

        var last = method.GetParameters().Last();

        Assert.Equal(typeof(CancellationToken), last.ParameterType);
        // AIFunctionFactory binds a trailing token itself rather than exposing it to the model,
        // so the tool schema the agent sees is unchanged by this.
        Assert.True(last.IsOptional);
    }

    [Fact]
    public async Task OrderAnomalies_PropagatesCancellationRatherThanReportingAnOutage()
    {
        var (order, _, _, _) = Build();
        using var cts = new CancellationTokenSource();
        var call = order.InvestigateOrderAnomalyAsync(null, cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
    }

    [Fact]
    public async Task OrderDetails_PropagatesCancellation()
    {
        var (order, _, _, _) = Build();
        using var cts = new CancellationTokenSource();
        var call = order.GetOrderDetailsAsync("ORD-0001", cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
    }

    [Fact]
    public async Task InventoryAlerts_PropagatesCancellation()
    {
        var (_, inventory, _, _) = Build();
        using var cts = new CancellationTokenSource();
        var call = inventory.GetInventoryAlertsAsync(false, cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
    }

    [Fact]
    public async Task InventoryLevel_PropagatesCancellation()
    {
        var (_, inventory, _, _) = Build();
        using var cts = new CancellationTokenSource();
        var call = inventory.GetInventoryLevelAsync("SKU-ELEC-001", cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
    }

    [Fact]
    public async Task ProductDetails_PropagatesCancellation()
    {
        var (_, _, product, _) = Build();
        using var cts = new CancellationTokenSource();
        var call = product.GetProductDetailsAsync("SKU-ELEC-001", cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
    }

    [Fact]
    public async Task ProductsByCategory_PropagatesCancellation()
    {
        var (_, _, product, _) = Build();
        using var cts = new CancellationTokenSource();
        var call = product.ListProductsByCategoryAsync("Electronics", cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
    }

    [Fact]
    public async Task TheTokenActuallyReachesTheDownstreamRequest()
    {
        var (order, _, _, handler) = Build();
        using var cts = new CancellationTokenSource();
        var call = order.InvestigateOrderAnomalyAsync(null, cts.Token);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);

        // Not merely accepted at the signature — forwarded far enough to cancel the HTTP call.
        Assert.True(handler.ObservedToken.IsCancellationRequested);
    }
}
