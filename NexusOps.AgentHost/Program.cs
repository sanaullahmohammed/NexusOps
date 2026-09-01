using MassTransit;
using Microsoft.OpenApi;
using NexusOps.AgentHost.Endpoints;
using NexusOps.AgentHost.Extensions;
using NexusOps.AgentHost.Services;
using NexusOps.AgentHost.Tools;
using Scalar.AspNetCore;
using NexusOps.AgentHost.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, ct) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "NexusOps Agent Host API",
            Version = "v1",
            Description = "AI agent orchestration endpoint. Accepts natural language prompts and returns model responses via Azure AI Foundry."
        };
        return Task.CompletedTask;
    });
});
builder.Services.AddHttpClient("order-service", client =>
    client.BaseAddress = new Uri("http://order-service"));
builder.Services.AddHttpClient("inventory-service", client =>
    client.BaseAddress = new Uri("http://inventory-service"));
builder.Services.AddHttpClient("product-service", client =>
    client.BaseAddress = new Uri("http://product-service"));

builder.AddRedisDistributedCache("redis");

// 002 FR-008 / 003 FR-008: refuse to start on a non-positive MaxTurns or SlidingExpirationMinutes.
// The previous guard covered MaxTurns only, so a zero SlidingExpirationMinutes started cleanly and
// then failed every store write, misreported as a Redis connection fault.
builder.Services.AddOptions<ConversationSessionOptions>()
    .Bind(builder.Configuration.GetSection(ConversationSessionOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<IConversationStore, RedisConversationStore>();

// AgentHost's only saga-facing surface: OrderTools resolves a request client from IClientFactory
// per call (see OrderTools.cs) and awaits the saga's response. No consumers, no saga logic — that
// all lives in NexusOps.WorkflowOrchestrator (Constitution Principle I).
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        var connectionString = builder.Configuration.GetConnectionString("rabbitmq");
        cfg.Host(new Uri(connectionString!));
        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddToolHandlers(builder.Configuration);
builder.Services.AddAgentServices(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapDefaultEndpoints();
app.MapChatEndpoints();

app.Run();
