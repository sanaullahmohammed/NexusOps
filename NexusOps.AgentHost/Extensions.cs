using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NexusOps.AgentHost.Configuration;
using NexusOps.AgentHost.Services;
using OpenAI.Chat;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.ClientModel;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
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

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        var otelBuilder = builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    .AddHttpClientInstrumentation();
            });

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            otelBuilder.UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(HealthEndpointPath);

        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }

    public static IServiceCollection AddAgentServices(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<AzureAIOptions>(config.GetSection("AzureAI"));

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AzureAIOptions>>().Value;

            if (string.IsNullOrWhiteSpace(opts.Endpoint))
            {
                throw new InvalidOperationException("AzureAI:Endpoint is required.");
            }

            if (string.IsNullOrWhiteSpace(opts.DeploymentName))
            {
                throw new InvalidOperationException("AzureAI:DeploymentName is required.");
            }

            if (string.IsNullOrWhiteSpace(opts.ApiKey))
            {
                throw new InvalidOperationException("AzureAI:ApiKey is required.");
            }

            return new AzureOpenAIClient(
                new Uri(opts.Endpoint),
                new ApiKeyCredential(opts.ApiKey));
        });

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AzureAIOptions>>().Value;
            var azureOpenAiClient = sp.GetRequiredService<AzureOpenAIClient>();

            ChatClient chatClient = azureOpenAiClient.GetChatClient(opts.DeploymentName);

            AIAgent agent = chatClient.AsAIAgent(
                name: opts.AgentName,
                instructions: opts.AgentInstructions);

            return agent;
        });

        services.AddSingleton<IAgentService, AgentService>();

        return services;
    }
}