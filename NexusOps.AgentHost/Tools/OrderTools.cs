using NexusOps.Contracts.Dtos;
using System.Net.Http.Json;

namespace NexusOps.AgentHost.Tools;

public sealed class OrderTools(IHttpClientFactory httpClientFactory, ILogger<OrderTools> logger)
{
    public async Task<ToolResult<OrderAnomaly[]>> InvestigateOrderAnomalyAsync(string? status = null)
    {
        try
        {
            var client = httpClientFactory.CreateClient("order-service");
            var url = string.IsNullOrWhiteSpace(status)
                ? "/orders/anomalies"
                : $"/orders/anomalies?status={Uri.EscapeDataString(status)}";

            var result = await client.GetFromJsonAsync<OrderAnomaly[]>(url);
            return ToolResult<OrderAnomaly[]>.Ok(result ?? []);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve order anomalies");
            return ToolResult<OrderAnomaly[]>.Fail("Order service is temporarily unavailable.");
        }
    }

    public async Task<ToolResult<OrderSummary>> GetOrderDetailsAsync(string orderId)
    {
        try
        {
            var client = httpClientFactory.CreateClient("order-service");
            var result = await client.GetFromJsonAsync<OrderSummary>($"/orders/{Uri.EscapeDataString(orderId)}");
            if (result is null)
            {
                return ToolResult<OrderSummary>.Fail($"Order {orderId} was not found.");
            }
            return ToolResult<OrderSummary>.Ok(result);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ToolResult<OrderSummary>.Fail($"Order {orderId} was not found.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve order details for {OrderId}", orderId);
            return ToolResult<OrderSummary>.Fail("Order service is temporarily unavailable.");
        }
    }
}
