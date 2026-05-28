using NexusOps.OrderService.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.MapDefaultEndpoints();
app.MapOrderEndpoints();

app.Run();
