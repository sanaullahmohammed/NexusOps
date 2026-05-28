using NexusOps.Contracts.Dtos;
using System.Net.Http.Json;

namespace NexusOps.AgentHost.Tools;

public sealed class ProductTools(IHttpClientFactory httpClientFactory, ILogger<ProductTools> logger)
{
    public async Task<ToolResult<ProductDetail>> GetProductDetailsAsync(string sku)
    {
        try
        {
            var client = httpClientFactory.CreateClient("product-service");
            var result = await client.GetFromJsonAsync<ProductDetail>($"/products/{Uri.EscapeDataString(sku)}");
            if (result is null)
            {
                return ToolResult<ProductDetail>.Fail($"Product with SKU {sku} was not found.");
            }
            return ToolResult<ProductDetail>.Ok(result);
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

    public async Task<ToolResult<ProductSummary[]>> ListProductsByCategoryAsync(string? category = null)
    {
        try
        {
            var client = httpClientFactory.CreateClient("product-service");
            var url = string.IsNullOrWhiteSpace(category)
                ? "/products"
                : $"/products?category={Uri.EscapeDataString(category)}";

            var result = await client.GetFromJsonAsync<ProductSummary[]>(url);
            return ToolResult<ProductSummary[]>.Ok(result ?? []);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve products for category {Category}", category ?? "all");
            return ToolResult<ProductSummary[]>.Fail("Product service is temporarily unavailable.");
        }
    }
}
