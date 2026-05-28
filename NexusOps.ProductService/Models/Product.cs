namespace NexusOps.ProductService.Models;

public sealed class Product
{
    public required string ProductId { get; init; }
    public required string Sku { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal WeightKg { get; init; }
}
