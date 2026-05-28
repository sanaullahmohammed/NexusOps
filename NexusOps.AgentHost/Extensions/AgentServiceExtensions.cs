using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;
using NexusOps.AgentHost.Configuration;
using NexusOps.AgentHost.Services;
using OpenAI.Chat;
using System.ClientModel;

namespace NexusOps.AgentHost.Extensions;

public static class AgentServiceExtensions
{
    public static IServiceCollection AddAgentServices(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<AzureAIOptions>(options =>
        {
            config.GetSection("AzureAI").Bind(options);
            if (string.IsNullOrWhiteSpace(options.ApiKey) || options.ApiKey == "<your-api-key>")
            {
                var envKey = config["AZURE_AI_FOUNDRY_API_KEY"];
                if (!string.IsNullOrWhiteSpace(envKey))
                {
                    options.ApiKey = envKey;
                }
            }
        });

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