var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("redis")
    .WithDataVolume();

var orderService = builder.AddProject<Projects.NexusOps_OrderService>("order-service")
    .WithHttpHealthCheck("/health");

var inventoryService = builder.AddProject<Projects.NexusOps_InventoryService>("inventory-service")
    .WithHttpHealthCheck("/health");

var productService = builder.AddProject<Projects.NexusOps_ProductService>("product-service")
    .WithHttpHealthCheck("/health");

var agentHost = builder.AddProject<Projects.NexusOps_AgentHost>("agent-host")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(orderService)
    .WithReference(inventoryService)
    .WithReference(productService)
    .WithReference(redis)
    .WaitFor(orderService)
    .WaitFor(inventoryService)
    .WaitFor(productService)
    .WaitFor(redis);

var server = builder.AddProject<Projects.NexusOps_Server>("server")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var webfrontend = builder.AddViteApp("webfrontend", "../frontend")
    .WithReference(server)
    .WaitFor(server);

server.PublishWithContainerFiles(webfrontend, "wwwroot");

builder.Build().Run();
