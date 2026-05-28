using NexusOps.Contracts.Dtos;
using NexusOps.ProductService.Data;

namespace NexusOps.ProductService.Endpoints;

public static class ProductEndpoints
{
    public static WebApplication MapProductEndpoints(this WebApplication app)
    {
        app.MapGet("/products/{sku}", (string sku) =>
        {
            var product = ProductStore.Products.FirstOrDefault(p =>
                string.Equals(p.Sku, sku, StringComparison.OrdinalIgnoreCase));

            if (product is null)
            {
                return Results.NotFound($"Product with SKU {sku} not found.");
            }

            var detail = new ProductDetail(
                ProductId: product.ProductId,
                Sku: product.Sku,
                Name: product.Name,
                Description: product.Description,
                Category: product.Category,
                UnitPrice: product.UnitPrice,
                WeightKg: product.WeightKg);

            return Results.Ok(detail);
        });

        app.MapGet("/products", (string? category) =>
        {
            var query = ProductStore.Products.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(category))
            {
                query = query.Where(p =>
                    string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase));
            }

            var summaries = query
                .Select(p => new ProductSummary(
                    ProductId: p.ProductId,
                    Sku: p.Sku,
                    Name: p.Name,
                    Category: p.Category,
                    UnitPrice: p.UnitPrice))
                .ToArray();

            return Results.Ok(summaries);
        });

        return app;
    }
}
