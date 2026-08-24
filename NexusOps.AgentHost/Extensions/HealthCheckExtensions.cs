using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace NexusOps.AgentHost.Extensions;

public static class HealthCheckExtensions
{
    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(HealthCheckConstants.HealthEndpointPath, new HealthCheckOptions
        {
            ResponseWriter = WriteHealthResponse
        });

        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks(HealthCheckConstants.AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("live"),
                ResponseWriter = WriteHealthResponse
            });
        }

        return app;
    }

    /// <summary>
    /// Emits the JSON body the service contracts document. The default writer returns the bare
    /// string "Healthy" as text/plain, which no documented consumer expects.
    /// </summary>
    private static Task WriteHealthResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsJsonAsync(new { status = report.Status.ToString().ToLowerInvariant() });
    }
}