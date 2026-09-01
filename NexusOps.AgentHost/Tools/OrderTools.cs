using MassTransit;
using NexusOps.Contracts.Dtos;
using NexusOps.Contracts.Messages;
using System.Net.Http.Json;

namespace NexusOps.AgentHost.Tools;

public sealed class OrderTools(
    IHttpClientFactory httpClientFactory,
    IClientFactory clientFactory,
    ILogger<OrderTools> logger)
{
    // OrderTools is a singleton (registered once, shared across requests), but MassTransit's
    // IRequestClient<T> is scoped -- a singleton cannot consume it directly. IClientFactory is
    // itself singleton-safe and exists precisely to create a request client from a component that
    // isn't inside a consumer's DI scope (here, an AgentHost tool handler).
    private static readonly RequestTimeout RootCauseTimeout = RequestTimeout.After(s: 8);

    public async Task<ToolResult<OrderAnomaly[]>> InvestigateOrderAnomalyAsync(
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient("order-service");
            var url = string.IsNullOrWhiteSpace(status)
                ? "/orders/anomalies"
                : $"/orders/anomalies?status={Uri.EscapeDataString(status)}";

            var result = await client.GetFromJsonAsync<OrderAnomaly[]>(url, cancellationToken);
            return ToolResult<OrderAnomaly[]>.Ok(result ?? []);
        }
        catch (OperationCanceledException)
        {
            // The caller went away. Not a service fault — let it propagate rather than reporting
            // an outage that never happened.
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            // A rejected argument is the agent's to correct, not an outage. Tell it what it may pass
            // so the next tool call can succeed, and log at Warning — this is not an incident.
            logger.LogWarning(ex, "Rejected anomaly status filter {Status}", status);
            return ToolResult<OrderAnomaly[]>.Fail(
                $"'{status}' is not a valid anomaly status. Valid values are: delayed, missing, payment-failed. Omit the filter to return every anomaly.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve order anomalies");
            return ToolResult<OrderAnomaly[]>.Fail("Order service is temporarily unavailable.");
        }
    }

    public async Task<ToolResult<OrderSummary>> GetOrderDetailsAsync(
        string orderId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient("order-service");
            var result = await client.GetFromJsonAsync<OrderSummary>($"/orders/{Uri.EscapeDataString(orderId)}", cancellationToken);
            if (result is null)
            {
                return ToolResult<OrderSummary>.Fail($"Order {orderId} was not found.");
            }
            return ToolResult<OrderSummary>.Ok(result);
        }
        catch (OperationCanceledException)
        {
            throw;
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

    public async Task<ToolResult<RootCauseInvestigationResult>> InvestigateOrderRootCauseAsync(
        string orderId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rootCauseClient = clientFactory.CreateRequestClient<InvestigateOrderRootCause>(RootCauseTimeout);
            var response = await rootCauseClient.GetResponse<RootCauseInvestigationResult>(
                new InvestigateOrderRootCause(orderId), cancellationToken);

            var result = response.Message;

            return result.Completeness == InvestigationCompleteness.Failed
                ? ToolResult<RootCauseInvestigationResult>.Fail(
                    $"The investigation for order {orderId} could not be completed: the order service did not respond.")
                : ToolResult<RootCauseInvestigationResult>.Ok(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RequestTimeoutException)
        {
            logger.LogWarning("Root-cause investigation for {OrderId} timed out waiting for the saga", orderId);
            return ToolResult<RootCauseInvestigationResult>.Fail(
                $"The investigation for order {orderId} timed out before a result was available.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to investigate root cause for {OrderId}", orderId);
            return ToolResult<RootCauseInvestigationResult>.Fail("The workflow orchestrator is temporarily unavailable.");
        }
    }
}
