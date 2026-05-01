var builder = DistributedApplication.CreateBuilder(args);

var _ = builder.AddProject<Projects.NexusOps_AgentHost>("agent-host")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithEnvironment("AzureAI__ApiKey",
        builder.Configuration["AZURE_AI_FOUNDRY_API_KEY"] ?? string.Empty);

var server = builder.AddProject<Projects.NexusOps_Server>("server")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var webfrontend = builder.AddViteApp("webfrontend", "../frontend")
    .WithReference(server)
    .WaitFor(server);

server.PublishWithContainerFiles(webfrontend, "wwwroot");

builder.Build().Run();
