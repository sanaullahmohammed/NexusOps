using NexusOps.OrderService.Data;

namespace NexusOps.Tests.Orders;

/// <summary>
/// Integrity checks on the in-memory order seed set. These hold today and are asserted
/// here so that the seed changes in feature 003 batch A cannot silently break them.
/// </summary>
public class OrderStoreTests
{
    [Fact]
    public void OrderIds_AreUnique()
    {
        var ids = OrderStore.Orders.Select(o => o.OrderId).ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryOrder_HasAtLeastOneLineItem()
    {
        var empty = OrderStore.Orders.Where(o => o.LineItems.Count == 0).Select(o => o.OrderId);

        Assert.Empty(empty);
    }

    [Fact]
    public void TotalAmount_EqualsSumOfLineItems()
    {
        foreach (var order in OrderStore.Orders)
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
        var delivered = OrderStore.Orders
            .Where(o => o.Status == OrderService.Models.OrderStatus.Delivered)
            .ToArray();

        Assert.NotEmpty(delivered);
        Assert.All(delivered, o => Assert.NotNull(o.ActualDelivery));
    }

    [Fact]
    public void UndeliveredOrders_HaveNoActualDeliveryDate()
    {
        var undelivered = OrderStore.Orders
            .Where(o => o.Status != OrderService.Models.OrderStatus.Delivered);

        Assert.All(undelivered, o => Assert.Null(o.ActualDelivery));
    }
}
