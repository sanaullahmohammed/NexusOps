using MassTransit;
using Microsoft.EntityFrameworkCore;
using NexusOps.WorkflowOrchestrator.OrderInvestigation;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<OrderInvestigationDbContext>("workfloworchestrator");

builder.Services.AddMassTransit(x =>
{
    x.AddOrderInvestigationSaga();

    x.UsingRabbitMq((context, cfg) =>
    {
        var connectionString = builder.Configuration.GetConnectionString("rabbitmq")
            ?? throw new InvalidOperationException("Missing required configuration: ConnectionStrings:rabbitmq");
        cfg.Host(new Uri(connectionString));

        // Retry a transient failure (including a saga optimistic-concurrency conflict, which
        // surfaces as a DbUpdateConcurrencyException from the EF Core repository) a few times
        // before it falls through to the broker's own redelivery/dead-letter handling.
        cfg.UseMessageRetry(r => r.Intervals(50, 100, 200, 500));

        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

// Applies pending EF Core migrations on startup -- Aspire provisions the empty database, but
// nothing else runs `dotnet ef database update` against it. This is the only migration/table
// this feature owns (data-model.md); a future saga applies its own migrations independently.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<OrderInvestigationDbContext>().Database.MigrateAsync();
}

// Unlike AgentHost and the domain services, this host structurally cannot do anything without the
// bus, so its readiness is supposed to reflect bus connectivity (research.md Decision 7).
app.MapDefaultEndpoints(includeMassTransitInReadiness: true);

app.Run();
