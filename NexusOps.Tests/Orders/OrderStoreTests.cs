using NexusOps.OrderService.Data;
using NexusOps.OrderService.Models;

namespace NexusOps.Tests.Orders;

/// <summary>
/// Integrity checks on the in-memory order seed set, resolved through a pinned clock so that
/// every date-derived value is deterministic.
/// </summary>
public class OrderStoreTests
{
    private static OrderStore Store() => new(FixedTimeProvider.Default);

    [Fact]
    public void OrderIds_AreUnique()
    {
        var ids = Store().Orders.Select(o => o.OrderId).ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryOrder_HasAtLeastOneLineItem()
    {
        var empty = Store().Orders.Where(o => o.LineItems.Count == 0).Select(o => o.OrderId);

        Assert.Empty(empty);
    }

    [Fact]
    public void TotalAmount_EqualsSumOfLineItems()
    {
        foreach (var order in Store().Orders)
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
        var delivered = Store().Orders.Where(o => o.Status == OrderStatus.Delivered).ToArray();

        Assert.NotEmpty(delivered);
        Assert.All(delivered, o => Assert.NotNull(o.ActualDelivery));
    }

    [Fact]
    public void UndeliveredOrders_HaveNoActualDeliveryDate()
    {
        var undelivered = Store().Orders.Where(o => o.Status != OrderStatus.Delivered);

        Assert.All(undelivered, o => Assert.Null(o.ActualDelivery));
    }

    [Fact]
    public void EveryAnomalyReason_IsRepresentedInTheSeedSet()
    {
        var represented = Store().Orders
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
        var orders = Store().Orders;

        Assert.Equal(11, orders.Count);
        Assert.Equal(4, orders.Count(o => o.AnomalyReason is not null));
    }

    [Fact]
    public void SeedDates_AreRelativeToTheProvidedClock()
    {
        var early = new OrderStore(new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        var late = new OrderStore(new FixedTimeProvider(new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        var earlyOrder = early.Orders.Single(o => o.OrderId == "ORD-0001");
        var lateOrder = late.Orders.Single(o => o.OrderId == "ORD-0001");

        // A year later on the wall clock, the order is still the same number of days overdue.
        Assert.Equal(365, lateOrder.ExpectedDelivery.DayNumber - earlyOrder.ExpectedDelivery.DayNumber);
    }
}
