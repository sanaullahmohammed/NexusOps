using MassTransit;
using NexusOps.Contracts.Dtos;
using NexusOps.Contracts.Messages;
using NexusOps.ProductService.Data;

namespace NexusOps.ProductService.Consumers;

/// <summary>
/// Answers the fan-out coordinator's batch product-detail lookup for a root-cause investigation.
/// A SKU with no product record doesn't fail the whole source — it's reported in
/// <c>SkusNotFound</c> (spec.md Edge Cases).
/// </summary>
public sealed class RequestProductFindingConsumer : IConsumer<RequestProductFinding>
{
    public Task Consume(ConsumeContext<RequestProductFinding> context)
    {
        var products = new List<ProductDetail>();
        var notFound = new List<string>();

        foreach (var sku in context.Message.Skus)
        {
            var product = ProductStore.Products.FirstOrDefault(p =>
                string.Equals(p.Sku, sku, StringComparison.OrdinalIgnoreCase));

            if (product is null)
            {
                notFound.Add(sku);
                continue;
            }

            products.Add(new ProductDetail(
                ProductId: product.ProductId,
                Sku: product.Sku,
                Name: product.Name,
                Description: product.Description,
                Category: product.Category,
                UnitPrice: product.UnitPrice,
                WeightKg: product.WeightKg));
        }

        var status = products.Count > 0 ? SourceFindingStatus.Succeeded : SourceFindingStatus.NotFound;

        return context.RespondAsync(new ProductFindingReported(
            context.Message.CorrelationId, status, products.ToArray(), notFound.ToArray()));
    }
}
