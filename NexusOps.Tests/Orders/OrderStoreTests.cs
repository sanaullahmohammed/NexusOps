using NexusOps.OrderService.Data;
using NexusOps.OrderService.Models;

namespace NexusOps.Tests.Orders;

/// <summary>
/// Integrity checks on the in-memory order seed set, resolved through a pinned clock so that
/// every date-derived value is deterministic.
/// </summary>
public class OrderStoreTests
{
    private static IReadOnlyList<Order> Store() => OrderStore.GetOrders(FixedTimeProvider.DefaultToday);

    [Fact]
    public void OrderIds_AreUnique()
    {
        var ids = Store().Select(o => o.OrderId).ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryOrder_HasAtLeastOneLineItem()
    {
        var empty = Store().Where(o => o.LineItems.Count == 0).Select(o => o.OrderId);

        Assert.Empty(empty);
    }

    [Fact]
    public void TotalAmount_EqualsSumOfLineItems()
    {
        foreach (var order in Store())
        {
            var computed = order.LineItems.Sum(li => li.Quantity * li.UnitPrice);

            Assert.True(
                computed == order.TotalAmount,
                $"{order.OrderId}: TotalAmount {order.TotalAmount} != line item sum {computed}");
        }
    }

    [Fact]
    public void DeliveredOrders_HaveAnActualDeliveryDate()
    {
        var delivered = Store().Where(o => o.Status == OrderStatus.Delivered).ToArray();

        Assert.NotEmpty(delivered);
        Assert.All(delivered, o => Assert.NotNull(o.ActualDelivery));
    }

    [Fact]
    public void UndeliveredOrders_HaveNoActualDeliveryDate()
    {
        var undelivered = Store().Where(o => o.Status != OrderStatus.Delivered);

        Assert.All(undelivered, o => Assert.Null(o.ActualDelivery));
    }

    [Fact]
    public void EveryAnomalyReason_IsRepresentedInTheSeedSet()
    {
        var represented = Store()
            .Where(o => o.AnomalyReason is not null)
            .Select(o => o.AnomalyReason!.Value)
            .Distinct();

        Assert.Equal(
            Enum.GetValues<AnomalyReason>().OrderBy(r => r),
            represented.OrderBy(r => r));
    }

    [Fact]
    public void OrdersWithNoAnomalyReason_AreTheNormalMajority()
    {
        var orders = Store();

        Assert.Equal(11, orders.Count);
        Assert.Equal(4, orders.Count(o => o.AnomalyReason is not null));
    }

    [Fact]
    public void SeedDates_AreRelativeToTheSuppliedDate()
    {
        var early = OrderStore.GetOrders(new DateOnly(2030, 1, 1));
        var late = OrderStore.GetOrders(new DateOnly(2031, 1, 1));

        var earlyOrder = early.Single(o => o.OrderId == "ORD-0001");
        var lateOrder = late.Single(o => o.OrderId == "ORD-0001");

        // A year later, the order is still the same number of days overdue.
        Assert.Equal(365, lateOrder.ExpectedDelivery.DayNumber - earlyOrder.ExpectedDelivery.DayNumber);
    }

    [Fact]
    public void TheMediumSeverityExample_StaysMediumHoweverLongTheHostRuns()
    {
        // Regression: the seed was once frozen at process start while the endpoint recomputed
        // "today" per request, so ORD-0002 — deliberately seeded 3 days overdue as the medium
        // example — began reporting high after roughly five days of uptime.
        foreach (var day in new[] { 0, 1, 5, 30, 365 })
        {
            var today = FixedTimeProvider.DefaultToday.AddDays(day);
            var order = OrderStore.GetOrders(today).Single(o => o.OrderId == "ORD-0002");
            var overdue = today.DayNumber - order.ExpectedDelivery.DayNumber;

            Assert.Equal(3, overdue);
        }
    }
}
