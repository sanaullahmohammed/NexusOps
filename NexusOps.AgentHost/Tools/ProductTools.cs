using NexusOps.Contracts.Dtos;
using System.Net.Http.Json;

namespace NexusOps.AgentHost.Tools;

public sealed class ProductTools(IHttpClientFactory httpClientFactory, ILogger<ProductTools> logger)
{
    public async Task<ToolResult<ProductDetail>> GetProductDetailsAsync(
        string sku,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient("product-service");
            var result = await client.GetFromJsonAsync<ProductDetail>($"/products/{Uri.EscapeDataString(sku)}", cancellationToken);
            if (result is null)
            {
                return ToolResult<ProductDetail>.Fail($"Product with SKU {sku} was not found.");
            }
            return ToolResult<ProductDetail>.Ok(result);
        }
        catch (OperationCanceledException)
        {
            // The caller went away. Not a service fault — let it propagate rather than reporting
            // an outage that never happened.
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ToolResult<ProductDetail>.Fail($"Product with SKU {sku} was not found.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve product details for {Sku}", sku);
            return ToolResult<ProductDetail>.Fail("Product service is temporarily unavailable.");
        }
    }

    public async Task<ToolResult<ProductSummary[]>> ListProductsByCategoryAsync(
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient("product-service");
            var url = string.IsNullOrWhiteSpace(category)
                ? "/products"
                : $"/products?category={Uri.EscapeDataString(category)}";

            var result = await client.GetFromJsonAsync<ProductSummary[]>(url, cancellationToken);
            return ToolResult<ProductSummary[]>.Ok(result ?? []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve products for category {Category}", category ?? "all");
            return ToolResult<ProductSummary[]>.Fail("Product service is temporarily unavailable.");
        }
    }
}
