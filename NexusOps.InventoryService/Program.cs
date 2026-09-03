using MassTransit;
using NexusOps.InventoryService.Consumers;
using NexusOps.InventoryService.Data;
using NexusOps.InventoryService.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<InventoryMutationOverlay>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<RequestInventoryFindingConsumer>();
    x.AddConsumer<ExecuteInventoryRestockConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var connectionString = builder.Configuration.GetConnectionString("rabbitmq")
            ?? throw new InvalidOperationException("Missing required configuration: ConnectionStrings:rabbitmq");
        cfg.Host(new Uri(connectionString));
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

app.UseExceptionHandler();
app.MapDefaultEndpoints();
app.MapInventoryEndpoints();

app.Run();
