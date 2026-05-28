namespace NexusOps.Contracts.Dtos;

public sealed record ProductDetail(
    string ProductId,
    string Sku,
    string Name,
    string Description,
    string Category,
    decimal UnitPrice,
    decimal WeightKg);

public sealed record ProductSummary(
    string ProductId,
    string Sku,
    string Name,
    string Category,
    decimal UnitPrice);
