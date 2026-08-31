using NexusOps.OrderService.Data;
using NexusOps.OrderService.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();

// Endpoints resolve "today" once per request through TimeProvider and pass it to the seed, so
// date-derived values stay plausible over time and stay deterministic under test.
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

app.UseExceptionHandler();
app.MapDefaultEndpoints();
app.MapOrderEndpoints();

app.Run();
