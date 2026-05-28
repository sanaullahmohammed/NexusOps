using NexusOps.Contracts.Dtos;
using System.Net.Http.Json;

namespace NexusOps.AgentHost.Tools;

public sealed class InventoryTools(IHttpClientFactory httpClientFactory, ILogger<InventoryTools> logger)
{
    public async Task<ToolResult<InventoryAlert[]>> GetInventoryAlertsAsync(bool outOfStockOnly = false)
    {
        try
        {
            var client = httpClientFactory.CreateClient("inventory-service");
            var url = outOfStockOnly
                ? "/inventory/alerts?outOfStockOnly=true"
                : "/inventory/alerts";

            var result = await client.GetFromJsonAsync<InventoryAlert[]>(url);
            return ToolResult<InventoryAlert[]>.Ok(result ?? []);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve inventory alerts");
            return ToolResult<InventoryAlert[]>.Fail("Inventory service is temporarily unavailable.");
        }
    }

    public async Task<ToolResult<InventoryLevel>> GetInventoryLevelAsync(string sku)
    {
        try
        {
            var client = httpClientFactory.CreateClient("inventory-service");
            var result = await client.GetFromJsonAsync<InventoryLevel>($"/inventory/{Uri.EscapeDataString(sku)}");
            if (result is null)
            {
                return ToolResult<InventoryLevel>.Fail($"Inventory record for SKU {sku} was not found.");
            }
            return ToolResult<InventoryLevel>.Ok(result);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ToolResult<InventoryLevel>.Fail($"Inventory record for SKU {sku} was not found.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve inventory level for {Sku}", sku);
            return ToolResult<InventoryLevel>.Fail("Inventory service is temporarily unavailable.");
        }
    }
}
