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
        app.MapHealthChecks(HealthCheckConstants.HealthEndpointPath);

        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks(HealthCheckConstants.AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("live")
            });
        }

        return app;
    }
}