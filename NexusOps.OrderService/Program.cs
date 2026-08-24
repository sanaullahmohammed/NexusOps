using NexusOps.OrderService.Data;
using NexusOps.OrderService.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();

// Seed data and the anomaly endpoint both resolve "today" through TimeProvider so that
// date-derived values stay plausible over time and stay deterministic under test.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<OrderStore>();

var app = builder.Build();

app.UseExceptionHandler();
app.MapDefaultEndpoints();
app.MapOrderEndpoints();

app.Run();
