using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace NexusOps.AgentHost.Extensions;

public static class HealthCheckExtensions
{
    /// <summary>Checks that gate readiness — a failure here means the service cannot serve requests.</summary>
    private const string ReadyTag = "ready";

    /// <summary>Checks that gate liveness — a failure here means the process should be restarted.</summary>
    private const string LiveTag = "live";

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: [LiveTag, ReadyTag]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Readiness reports only checks tagged "ready".
        //
        // This predicate is load-bearing. `AddRedisDistributedCache` registers its own health check
        // with failureStatus Unhealthy and no tags; without a predicate the aggregate would include
        // it, and a Redis blip would return 503 — the AppHost would mark this service unhealthy and
        // a Kubernetes readiness probe would pull the pod from rotation.
        //
        // That would be wrong. The Agent Host is explicitly designed to keep serving when the
        // conversation store is unreachable: HistoryOutcome.Unavailable preserves the caller's
        // session and processes the turn statelessly (002 FR-010, FR-013). Failing readiness for a
        // dependency the service is built to survive would take the process out of rotation
        // precisely when it is still able to answer.
        //
        // Redis health remains visible — it is its own resource in the AppHost, with its own probe.
        app.MapHealthChecks(HealthCheckConstants.HealthEndpointPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(ReadyTag),
            ResponseWriter = WriteHealthResponse
        });

        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks(HealthCheckConstants.AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains(LiveTag),
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
