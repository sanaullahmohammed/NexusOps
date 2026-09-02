var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("redis")
    .WithDataVolume();

var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin()
    .WithDataVolume();

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();

var workflowOrchestratorDb = postgres.AddDatabase("workfloworchestrator");

var orderService = builder.AddProject<Projects.NexusOps_OrderService>("order-service")
    .WithHttpHealthCheck("/health")
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

var inventoryService = builder.AddProject<Projects.NexusOps_InventoryService>("inventory-service")
    .WithHttpHealthCheck("/health")
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

var productService = builder.AddProject<Projects.NexusOps_ProductService>("product-service")
    .WithHttpHealthCheck("/health")
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq);

var workflowOrchestrator = builder.AddProject<Projects.NexusOps_WorkflowOrchestrator>("workflow-orchestrator")
    .WithHttpHealthCheck("/health")
    .WithReference(rabbitmq)
    .WithReference(workflowOrchestratorDb)
    .WaitFor(rabbitmq)
    .WaitFor(workflowOrchestratorDb);

var agentHost = builder.AddProject<Projects.NexusOps_AgentHost>("agent-host")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(orderService)
    .WithReference(inventoryService)
    .WithReference(productService)
    .WithReference(redis)
    .WithReference(rabbitmq)
    .WaitFor(orderService)
    .WaitFor(inventoryService)
    .WaitFor(productService)
    .WaitFor(redis)
    .WaitFor(rabbitmq);

var server = builder.AddProject<Projects.NexusOps_Server>("server")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var webfrontend = builder.AddViteApp("webfrontend", "../frontend")
    .WithReference(server)
    .WaitFor(server);

server.PublishWithContainerFiles(webfrontend, "wwwroot");

builder.Build().Run();
