namespace NexusOps.Contracts.Dtos;

/// <summary>
/// An order in an abnormal state, as returned by <c>GET /orders/anomalies</c>.
/// </summary>
/// <param name="OrderId">The order's identifier.</param>
/// <param name="AnomalyType">Why the order is anomalous: <c>delayed</c>, <c>missing</c> or <c>payment-failed</c>. Derived from the order itself, never from the query that selected it.</param>
/// <param name="Severity">Triage weight: <c>high</c> or <c>medium</c>.</param>
/// <param name="DaysOverdue">Days past expected delivery, for delayed orders; <c>null</c> otherwise.</param>
/// <param name="CustomerId">The customer who placed the order.</param>
/// <param name="TotalAmount">Order value, so impact can be weighed without a second call.</param>
/// <param name="ExpectedDelivery">The delivery date that was promised.</param>
/// <param name="LineItems">The order's line items. Carries the SKUs that let an anomaly be correlated against inventory alerts without a per-order round trip.</param>
public sealed record OrderAnomaly(
    string OrderId,
    string AnomalyType,
    string Severity,
    int? DaysOverdue,
    string CustomerId,
    decimal TotalAmount,
    DateOnly ExpectedDelivery,
    OrderLineItem[] LineItems);
