using NexusOps.InventoryService.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.MapDefaultEndpoints();
app.MapInventoryEndpoints();

app.Run();
