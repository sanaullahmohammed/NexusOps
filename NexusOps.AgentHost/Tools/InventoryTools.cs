using NexusOps.Contracts.Dtos;
using System.Net.Http.Json;

namespace NexusOps.AgentHost.Tools;

public sealed class InventoryTools(IHttpClientFactory httpClientFactory, ILogger<InventoryTools> logger)
{
    public async Task<ToolResult<InventoryAlert[]>> GetInventoryAlertsAsync(
        bool outOfStockOnly = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient("inventory-service");
            var url = outOfStockOnly
                ? "/inventory/alerts?outOfStockOnly=true"
                : "/inventory/alerts";

            var result = await client.GetFromJsonAsync<InventoryAlert[]>(url, cancellationToken);
            return ToolResult<InventoryAlert[]>.Ok(result ?? []);
        }
        catch (OperationCanceledException)
        {
            // The caller went away. Not a service fault — let it propagate rather than reporting
            // an outage that never happened.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve inventory alerts");
            return ToolResult<InventoryAlert[]>.Fail("Inventory service is temporarily unavailable.");
        }
    }

    public async Task<ToolResult<InventoryLevel>> GetInventoryLevelAsync(
        string sku,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient("inventory-service");
            var result = await client.GetFromJsonAsync<InventoryLevel>($"/inventory/{Uri.EscapeDataString(sku)}", cancellationToken);
            if (result is null)
            {
                return ToolResult<InventoryLevel>.Fail($"Inventory record for SKU {sku} was not found.");
            }
            return ToolResult<InventoryLevel>.Ok(result);
        }
        catch (OperationCanceledException)
        {
            throw;
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
