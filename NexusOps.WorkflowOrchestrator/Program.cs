using MassTransit;
using Microsoft.EntityFrameworkCore;
using NexusOps.WorkflowOrchestrator.OrderAction;
using NexusOps.WorkflowOrchestrator.OrderInvestigation;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<OrderInvestigationDbContext>("workfloworchestrator");
builder.AddNpgsqlDbContext<OrderActionDbContext>("workfloworchestrator");

builder.Services.AddMassTransit(x =>
{
    x.AddOrderInvestigationSaga();
    x.AddOrderActionSaga();

    x.UsingRabbitMq((context, cfg) =>
    {
        var connectionString = builder.Configuration.GetConnectionString("rabbitmq")
            ?? throw new InvalidOperationException("Missing required configuration: ConnectionStrings:rabbitmq");
        cfg.Host(new Uri(connectionString));

        // Retry a transient failure (including a saga optimistic-concurrency conflict, which
        // surfaces as a DbUpdateConcurrencyException from the EF Core repository) a few times
        // before it falls through to the broker's own redelivery/dead-letter handling.
        cfg.UseMessageRetry(r => r.Intervals(50, 100, 200, 500));

        // Excludes OrderActionSagaState and OrderInvestigationSagaState from this sweep's
        // auto-discovery -- a filter scoped to this one ConfigureEndpoints call, not a type-level
        // [ExcludeFromConfigureEndpoints] attribute (an earlier version of this fix used the
        // attribute; it unconditionally excluded the saga from every registration context
        // project-wide, including MassTransit's own test harness in NexusOps.Tests, which broke all
        // of it silently until the suite was re-run). Both endpoints are configured manually below
        // instead, so each can carry its own UseEntityFrameworkOutbox.
        cfg.ConfigureEndpoints(context, x =>
        {
            x.Exclude<OrderActionSagaState>();
            x.Exclude<OrderInvestigationSagaState>();
        });

        // UseBusOutbox() (configured in OrderAction/ServiceCollectionExtensions.cs) only covers
        // ISendEndpointProvider/IPublishEndpoint calls made OUTSIDE of a consume context -- per
        // MassTransit's own doc comment on IEntityFrameworkOutboxConfigurator.UseBusOutbox, it
        // explicitly does not apply "when consuming messages". OrderActionSaga.Publish(...) calls
        // (e.g. BeginOrderActionExecution on Approve) run *inside* a consume context, so without a
        // receive-endpoint-level outbox, a retried DbUpdateConcurrencyException on the saga's own
        // SaveChanges does not roll back a Publish that already reached the broker -- two
        // concurrent Approve attempts could each publish BeginOrderActionExecution once, even
        // though only one attempt's state transition ultimately commits. UseEntityFrameworkOutbox
        // only has an IReceiveEndpointConfigurator overload (not a bus-wide one), so the saga's
        // endpoint is configured manually here, ensuring every publish made while consuming on this
        // one endpoint shares the saga's own SaveChanges transaction.
        cfg.ReceiveEndpoint("OrderActionSagaState", e =>
        {
            e.UseEntityFrameworkOutbox<OrderActionDbContext>(context);
            e.ConfigureSaga<OrderActionSagaState>(context);
        });

        // Same reasoning as OrderActionSagaState above, closing the identical race for feature 005's
        // saga (008-order-investigation-outbox research.md Decision 2): Initially(When(Requested))'s
        // Publish(BeginInvestigationFanOut) runs inside a consume context, so without a
        // receive-endpoint-level outbox it can reach the broker -- and a reply to it can come back
        // and be discarded by OnMissingInstance -- before this SaveChanges has actually committed
        // the new saga row.
        cfg.ReceiveEndpoint("OrderInvestigationSagaState", e =>
        {
            e.UseEntityFrameworkOutbox<OrderInvestigationDbContext>(context);
            e.ConfigureSaga<OrderInvestigationSagaState>(context);
        });
    });
});

var app = builder.Build();

// Applies pending EF Core migrations on startup -- Aspire provisions the empty database, but
// nothing else runs `dotnet ef database update` against it. Each saga owns its own migration set
// in its own DbContext (data-model.md); a future saga applies its own migrations independently.
// Order matters here: OrderActionDbContext's own InboxState/OutboxState/OutboxMessage migration
// (feature 006) is a plain, non-idempotent CREATE TABLE, while OrderInvestigationDbContext's
// equivalent migration is CREATE TABLE IF NOT EXISTS (008-order-investigation-outbox research.md
// Decision 4 -- both contexts share these physical tables). Running OrderAction first means it
// always creates them normally; OrderInvestigation's migration then finds them already present and
// no-ops. Should OrderAction ever be deleted (this project's own domain-pluggability precedent
// permits it), OrderInvestigation's migration becomes the one that actually creates the tables --
// self-healing either way, but only if OrderAction runs first whenever it exists.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<OrderActionDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<OrderInvestigationDbContext>().Database.MigrateAsync();
}

// Unlike AgentHost and the domain services, this host structurally cannot do anything without the
// bus, so its readiness is supposed to reflect bus connectivity (research.md Decision 7).
app.MapDefaultEndpoints(includeMassTransitInReadiness: true);

app.Run();
