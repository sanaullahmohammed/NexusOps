using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexusOps.Contracts.Messages;

namespace NexusOps.IntegrationTests;

/// <summary>
/// Boots the real <c>NexusOps.AppHost</c> topology via <c>Aspire.Hosting.Testing</c> against real
/// RabbitMQ and PostgreSQL containers and the real <c>NexusOps.WorkflowOrchestrator</c>,
/// <c>NexusOps.OrderService</c>, <c>NexusOps.InventoryService</c>, and <c>NexusOps.ProductService</c>
/// processes -- one instance shared by every test in <see cref="WorkflowOrchestratorIntegrationTests"/>
/// (ROADMAP.md Prompt 6).
/// </summary>
/// <remarks>
/// <c>agent-host</c>, <c>server</c>, <c>webfrontend</c>, and <c>notification-service</c> are removed
/// from the application model before it is built: <c>agent-host</c> fails
/// <c>ValidateOnStart()</c> without real Azure AI credentials (<c>AgentServiceExtensions.cs</c>),
/// and <c>server</c>/<c>webfrontend</c>/<c>notification-service</c> need npm tooling this suite has
/// no reason to depend on. <c>redis</c> is removed alongside them since nothing but
/// <c>agent-host</c> references it. What remains -- <c>rabbitmq</c>, <c>postgres</c>, the three
/// domain services, and <c>workflow-orchestrator</c> -- is exactly the message-bus boundary these
/// tests exercise directly via a MassTransit bus of their own, the same request/response pattern
/// <c>NexusOps.AgentHost/Tools/OrderTools.cs</c> and <c>Endpoints/ApprovalEndpoints.cs</c> use, so
/// no Azure AI credential is ever required (ROADMAP.md's credential-free CI constraint).
/// <c>ContainerMountAnnotation</c> is also stripped from every remaining resource (today: just
/// <c>postgres</c> and <c>rabbitmq</c>, the only two carrying <c>WithDataVolume()</c> in
/// <c>AppHost.cs</c>) -- Aspire derives that volume's name from
/// the AppHost project itself, not from how it was launched -- so this suite would otherwise share
/// the exact same named volume as a developer's own <c>dotnet run --project NexusOps.AppHost</c>,
/// failing Postgres startup outright if both run at once, and inheriting stale saga/outbox rows and
/// durable queues from prior dev sessions even when run alone.
/// </remarks>
public sealed class WorkflowOrchestratorFixture : IAsyncLifetime
{
    private static readonly string[] ResourcesToRemove =
    [
        "agent-host", "server", "webfrontend", "notification-service", "redis"
    ];

    private static readonly string[] ResourcesToWaitFor =
    [
        "rabbitmq", "postgres", "order-service", "inventory-service", "product-service", "workflow-orchestrator"
    ];

    // Generous headroom above production's own request-client timeouts (OrderTools.cs:
    // RootCauseTimeout=12s, ActionRequestTimeout=10s; AgentHost/Program.cs: Approve=25s, Reject=5s) --
    // a cold container/process start under CI load is slower than a warm local host, and these tests
    // must not flake on infrastructure latency the production timeouts were never sized for.
    private static readonly RequestTimeout InvestigationTimeout = RequestTimeout.After(s: 30);
    private static readonly RequestTimeout ActionRequestTimeout = RequestTimeout.After(s: 30);
    private static readonly RequestTimeout ApproveTimeout = RequestTimeout.After(s: 45);
    private static readonly RequestTimeout RejectTimeout = RequestTimeout.After(s: 20);

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private DistributedApplication _app = null!;
    private IHost _busHost = null!;

    public DistributedApplication App => _app;

    public IRequestClient<InvestigateOrderRootCause> RootCauseClient =>
        _busHost.Services.GetRequiredService<IRequestClient<InvestigateOrderRootCause>>();

    public IRequestClient<RequestOrderRefund> RefundClient =>
        _busHost.Services.GetRequiredService<IRequestClient<RequestOrderRefund>>();

    public IRequestClient<RequestOrderCancellation> CancellationClient =>
        _busHost.Services.GetRequiredService<IRequestClient<RequestOrderCancellation>>();

    public IRequestClient<ApproveOrderAction> ApproveClient =>
        _busHost.Services.GetRequiredService<IRequestClient<ApproveOrderAction>>();

    public IRequestClient<RejectOrderAction> RejectClient =>
        _busHost.Services.GetRequiredService<IRequestClient<RejectOrderAction>>();

    public HttpClient CreateOrderServiceClient() => _app.CreateHttpClient("order-service");

    /// <summary>Stops a domain service resource and waits for it to fully exit.</summary>
    public async Task StopResourceAsync(string resourceName)
    {
        var result = await _app.ResourceCommands.ExecuteCommandAsync(resourceName, KnownResourceCommands.StopCommand);
        Assert.True(result.Success, $"Failed to stop resource '{resourceName}': {result.Message}");
        // A stopped .NET project resource (as opposed to a container) doesn't necessarily land on
        // "Exited" specifically -- wait for any of the known terminal states instead.
        await _app.ResourceNotifications.WaitForResourceAsync(resourceName, KnownResourceStates.TerminalStates)
            .WaitAsync(StartupTimeout);
    }

    /// <summary>Restarts a previously-stopped domain service resource and waits for it to become healthy again.</summary>
    public async Task StartResourceAsync(string resourceName)
    {
        var result = await _app.ResourceCommands.ExecuteCommandAsync(resourceName, KnownResourceCommands.StartCommand);
        Assert.True(result.Success, $"Failed to start resource '{resourceName}': {result.Message}");
        await _app.ResourceNotifications.WaitForResourceHealthyAsync(resourceName)
            .WaitAsync(StartupTimeout);
    }

    public async Task InitializeAsync()
    {
        // xUnit v2 does not reliably call DisposeAsync() when IAsyncLifetime.InitializeAsync()
        // itself throws (a fixture that never finished initializing is not treated as needing
        // teardown) -- without this try/catch, a health-wait timeout or a missing connection string
        // here would leak the containers and the four spawned dotnet processes past the test run,
        // for someone to notice next only as mysteriously-already-running ports or a stale Docker
        // volume. Clean up whatever was actually started before rethrowing.
        try
        {
            var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.NexusOps_AppHost>();

            foreach (var name in ResourcesToRemove)
            {
                if (appHost.Resources.TryGetByName(name, out var resource))
                {
                    appHost.Resources.Remove(resource);
                }
            }

            // Strip every remaining resource's WithDataVolume() mount (currently postgres and
            // rabbitmq carry one) so this suite never touches a developer's own
            // dotnet run --project NexusOps.AppHost data (see the class-level remarks). Applied to
            // every resource, not a hardcoded name list, so a future resource gaining
            // WithDataVolume() in AppHost.cs can't silently regress to sharing that volume too.
            foreach (var resource in appHost.Resources)
            {
                foreach (var mount in resource.Annotations.OfType<ContainerMountAnnotation>().ToList())
                {
                    resource.Annotations.Remove(mount);
                }
            }

            _app = await appHost.BuildAsync();
            await _app.StartAsync();

            foreach (var resourceName in ResourcesToWaitFor)
            {
                await _app.ResourceNotifications.WaitForResourceHealthyAsync(resourceName).WaitAsync(StartupTimeout);
            }

            var rabbitConnectionString = await _app.GetConnectionStringAsync("rabbitmq")
                ?? throw new InvalidOperationException("The test RabbitMQ resource did not publish a connection string.");

            // Mirrors AgentHost's own bus setup (Program.cs) exactly: request clients only, no
            // consumers of our own, so this test process talks to the sagas over the real bus the
            // same way AgentHost does -- the message-bus boundary itself, not AgentHost's HTTP/LLM
            // layer.
            var busHostBuilder = Host.CreateApplicationBuilder();
            busHostBuilder.Services.AddMassTransit(x =>
            {
                x.AddRequestClient<InvestigateOrderRootCause>(InvestigationTimeout);
                x.AddRequestClient<RequestOrderRefund>(ActionRequestTimeout);
                x.AddRequestClient<RequestOrderCancellation>(ActionRequestTimeout);
                x.AddRequestClient<ApproveOrderAction>(ApproveTimeout);
                x.AddRequestClient<RejectOrderAction>(RejectTimeout);

                x.UsingRabbitMq((context, cfg) =>
                {
                    cfg.Host(new Uri(rabbitConnectionString));
                    cfg.ConfigureEndpoints(context);
                });
            });

            _busHost = busHostBuilder.Build();
            await _busHost.StartAsync();
        }
        catch
        {
            // Best-effort teardown -- a partially-started app can itself throw while disposing
            // (e.g. a container that's still mid-startup), and that secondary failure must never
            // replace the real reason initialization failed.
            try
            {
                await DisposeAsync();
            }
            catch
            {
                // Swallowed deliberately: the original exception below is the one that matters.
            }

            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (_busHost is not null)
        {
            await _busHost.StopAsync();
            _busHost.Dispose();
        }

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}
