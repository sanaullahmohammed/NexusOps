using System.Collections.Concurrent;
using NexusOps.OrderService.Models;

namespace NexusOps.OrderService.Data;

/// <summary>One order's recorded override: the status an approval-gated action set, and, for a refund, the amount actually applied.</summary>
public sealed record OrderMutationOverride(OrderStatus Status, decimal? RefundedAmount);

/// <summary>
/// Process-lifetime-durable mutation state layered on top of <see cref="OrderStore"/>'s otherwise
/// stateless, regenerated-per-call seed data — the minimal-touch way to give this feature's
/// refund/cancellation a real, observable effect without restructuring the proven seed/test
/// surface feature 001/005 already depend on (research.md Decision 7).
/// </summary>
public sealed class OrderMutationOverlay
{
    private readonly ConcurrentDictionary<string, OrderMutationOverride> _overrides = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string orderId, out OrderMutationOverride @override) => _overrides.TryGetValue(orderId, out @override!);

    public void Set(string orderId, OrderStatus status, decimal? refundedAmount = null) =>
        _overrides[orderId] = new OrderMutationOverride(status, refundedAmount);
}

public static class OrderMutationOverlayExtensions
{
    /// <summary>Returns <paramref name="order"/> with its status (and refunded amount) replaced by any recorded override, or unchanged if none exists.</summary>
    public static Order ApplyOverlay(this Order order, OrderMutationOverlay overlay) =>
        overlay.TryGet(order.OrderId, out var @override) ? order.WithStatus(@override.Status, @override.RefundedAmount) : order;
}
