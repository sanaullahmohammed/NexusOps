using Microsoft.OpenApi;
using NexusOps.AgentHost.Endpoints;
using Scalar.AspNetCore;

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
