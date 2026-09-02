using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Shared Aspire service defaults: service discovery, resilience, health checks, OpenTelemetry.
// Referenced by every service project. See https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });
        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath) &&
                            !context.Request.Path.StartsWithSegments(AlivenessEndpointPath);
                    })
                    .AddHttpClientInstrumentation();
            });

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live", "ready"]);
        return builder;
    }

    /// <param name="includeMassTransitInReadiness">
    /// MassTransit auto-registers a bus health check ("masstransit-bus", tagged "ready" and
    /// "masstransit") the moment a service calls <c>AddMassTransit</c>. For AgentHost and the
    /// domain services, the bus is one of several capabilities, not the reason they exist — their
    /// Direct-path HTTP endpoints work fine with the broker down, so a broker blip must not pull
    /// them out of rotation, exactly the reasoning CLAUDE.md already applies to the Redis
    /// conversation store. Default <c>false</c> excludes the "masstransit" tag from readiness.
    /// NexusOps.WorkflowOrchestrator passes <c>true</c>: it structurally cannot do anything without
    /// the bus, so its readiness is supposed to reflect that (research.md Decision 7).
    /// </param>
    public static WebApplication MapDefaultEndpoints(this WebApplication app, bool includeMassTransitInReadiness = false)
    {
        var isReady = includeMassTransitInReadiness
            ? (Func<HealthCheckRegistration, bool>)(r => r.Tags.Contains("ready"))
            : r => r.Tags.Contains("ready") && !r.Tags.Contains("masstransit");

        // Readiness is mapped in every environment. Exposing health endpoints publicly does carry
        // the security implications the Aspire template warns about
        // (https://aka.ms/dotnet/aspire/healthchecks), but the AppHost probes this path and WaitFors
        // it unconditionally, so gating it to Development left any non-Development start unable to
        // ever reach a healthy state. The endpoint stays; restrict reachability at the ingress.
        app.MapHealthChecks(HealthEndpointPath, new HealthCheckOptions
        {
            Predicate = isReady,
            ResponseWriter = WriteHealthResponse
        });

        // Liveness remains Development-only — nothing outside the dashboard consumes it.
        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live"),
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
