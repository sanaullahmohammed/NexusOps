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
builder.Services.Configure<ConversationSessionOptions>(builder.Configuration.GetSection(ConversationSessionOptions.SectionName));

// FR-008: fail at startup if MaxTurns is configured to 0 or a negative value
var sessionSection = builder.Configuration.GetSection(ConversationSessionOptions.SectionName);
var maxTurns = sessionSection.GetValue<int>("MaxTurns", 20);
if (maxTurns <= 0)
    throw new InvalidOperationException($"Session:MaxTurns must be a positive integer. Configured value '{maxTurns}' is invalid.");

builder.Services.AddSingleton<IConversationStore, RedisConversationStore>();

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
