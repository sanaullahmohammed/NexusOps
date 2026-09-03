using MassTransit;
using Microsoft.OpenApi;
using NexusOps.AgentHost.Endpoints;
using NexusOps.AgentHost.Extensions;
using NexusOps.AgentHost.Services;
using NexusOps.AgentHost.Tools;
using NexusOps.Contracts.Messages;
using Scalar.AspNetCore;
using NexusOps.AgentHost.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();

// The approval endpoints are the first place this system returns a raw enum in an HTTP JSON
// response (every other API surface's DTOs go through the AI SDK's own function-calling
// serialization, not ASP.NET's). Without this, OrderActionDecisionResult.DecisionStatus/
// ExecutionOutcome serialize as bare integers, contradicting contracts/order-action-tools.md's
// documented string values and making a curl response unreadable without cross-referencing the enum.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
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
    // Approval endpoints get proper per-request scoped DI (unlike the singleton OrderTools), so
    // their request clients are registered directly rather than minted per call via IClientFactory
    // (research.md Decision 3). Timeouts per contracts/saga-message-contracts.md's budget table:
    // approval blocks for the worst-case execution chain (order + inventory + compensation legs,
    // 5s each = 15s) plus headroom; rejection responds immediately, so a much shorter timeout
    // suffices. Widened from an initial 20s (zero headroom above the 15s worst case, the same
    // "single leg's figure, not the true worst case" mistake 005's research.md Decision 2 already
    // corrected once for RootCauseTimeout, and this feature's own ActionRequestTimeout already
    // repeated once live -- code review finding, fixed here before it recurred live a third time).
    x.AddRequestClient<ApproveOrderAction>(RequestTimeout.After(s: 25));
    x.AddRequestClient<RejectOrderAction>(RequestTimeout.After(s: 5));

    x.UsingRabbitMq((context, cfg) =>
    {
        var connectionString = builder.Configuration.GetConnectionString("rabbitmq")
            ?? throw new InvalidOperationException("Missing required configuration: ConnectionStrings:rabbitmq");
        cfg.Host(new Uri(connectionString));
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
app.MapApprovalEndpoints();

app.Run();
