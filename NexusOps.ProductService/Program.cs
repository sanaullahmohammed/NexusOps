using MassTransit;
using NexusOps.ProductService.Consumers;
using NexusOps.ProductService.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<RequestProductFindingConsumer>();

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
app.MapProductEndpoints();

app.Run();
