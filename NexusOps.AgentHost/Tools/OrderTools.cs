using MassTransit;
using NexusOps.Contracts.Dtos;
using NexusOps.Contracts.Messages;
using System.Net.Http.Json;

namespace NexusOps.AgentHost.Tools;

public sealed class OrderTools(
    IHttpClientFactory httpClientFactory,
    // OrderTools is a singleton (registered once, shared across requests), but MassTransit's
    // IRequestClient<T> is scoped -- a singleton cannot consume it directly. IClientFactory is
    // itself singleton-safe and exists precisely to create a request client from a component that
    // isn't inside a consumer's DI scope (here, an AgentHost tool handler).
    IClientFactory clientFactory,
    ILogger<OrderTools> logger)
{
    // InvestigationFanOutConsumer's own per-source timeout is 5s (order lookup, then inventory and
    // product in parallel) -- worst case that's 5s + 5s = 10s if the order lookup itself is slow and
    // one of the parallel legs also times out. This must exceed that worst case, not just the 5s
    // single-leg figure: an 8s client timeout would let the caller see "investigation timed out"
    // for a case the saga was about to answer correctly with a Degraded result.
    private static readonly RequestTimeout RootCauseTimeout = RequestTimeout.After(s: 12);

    // Covers validation's own 5s per-leg timeout plus transit/serialization headroom
    // (contracts/saga-message-contracts.md's Timeout Budget table, Leg 1). Widened from an initial
    // 8s after live verification observed an occasional spurious timeout under host load with no
    // corresponding broker backlog -- the same "size above the true worst case, not a single leg's
    // figure" correction 005's research.md Decision 2 made for its own root-cause timeout.
    private static readonly RequestTimeout ActionRequestTimeout = RequestTimeout.After(s: 10);

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

    public async Task<ToolResult<OrderActionRequestResult>> RequestOrderRefundAsync(
        string orderId,
        decimal? amount = null,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = clientFactory.CreateRequestClient<RequestOrderRefund>(ActionRequestTimeout);
            var response = await client.GetResponse<OrderActionRequestResult>(
                new RequestOrderRefund(orderId, amount, reason), cancellationToken);

            return response.Message.Status switch
            {
                OrderActionStatus.NotFound => ToolResult<OrderActionRequestResult>.Fail(
                    $"Order {orderId} was not found. No refund request was created."),
                OrderActionStatus.Unavailable => ToolResult<OrderActionRequestResult>.Fail(
                    $"Could not confirm order {orderId} exists — the order service was unavailable. No refund request was created; please retry."),
                _ => ToolResult<OrderActionRequestResult>.Ok(response.Message)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RequestTimeoutException)
        {
            logger.LogWarning("Refund request for {OrderId} timed out waiting for the saga", orderId);
            return ToolResult<OrderActionRequestResult>.Fail($"The refund request for order {orderId} timed out before a reference was available.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to request refund for {OrderId}", orderId);
            return ToolResult<OrderActionRequestResult>.Fail("The workflow orchestrator is temporarily unavailable.");
        }
    }

    public async Task<ToolResult<OrderActionRequestResult>> RequestOrderCancellationAsync(
        string orderId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = clientFactory.CreateRequestClient<RequestOrderCancellation>(ActionRequestTimeout);
            var response = await client.GetResponse<OrderActionRequestResult>(
                new RequestOrderCancellation(orderId, reason), cancellationToken);

            return response.Message.Status switch
            {
                OrderActionStatus.NotFound => ToolResult<OrderActionRequestResult>.Fail(
                    $"Order {orderId} was not found. No cancellation request was created."),
                OrderActionStatus.Unavailable => ToolResult<OrderActionRequestResult>.Fail(
                    $"Could not confirm order {orderId} exists — the order service was unavailable. No cancellation request was created; please retry."),
                _ => ToolResult<OrderActionRequestResult>.Ok(response.Message)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RequestTimeoutException)
        {
            logger.LogWarning("Cancellation request for {OrderId} timed out waiting for the saga", orderId);
            return ToolResult<OrderActionRequestResult>.Fail($"The cancellation request for order {orderId} timed out before a reference was available.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to request cancellation for {OrderId}", orderId);
            return ToolResult<OrderActionRequestResult>.Fail("The workflow orchestrator is temporarily unavailable.");
        }
    }
}
