using NexusOps.OrderService.Anomalies;
using NexusOps.OrderService.Data;
using NexusOps.OrderService.Models;

namespace NexusOps.Tests.Orders;

/// <summary>
/// Covers 003 FR-001 to FR-005: an order's anomaly classification is a property of the order,
/// the payload carries a join key, and severity means something.
/// </summary>
public class AnomalySelectorTests
{
    private static readonly DateOnly Today = FixedTimeProvider.DefaultToday;

    private static IReadOnlyList<Order> SeedOrders() => OrderStore.GetOrders(Today);

    private static OrderAnomalyView Select(AnomalyReason? filter) =>
        new(AnomalySelector.Select(SeedOrders(), filter, Today));

    private sealed record OrderAnomalyView(Contracts.Dtos.OrderAnomaly[] Results)
    {
        public string[] Ids => Results.Select(r => r.OrderId).ToArray();
    }

    private static Order OrderWith(AnomalyReason reason, int daysPastExpected) => new()
    {
        OrderId = "ORD-TEST",
        CustomerId = "CUST-TEST",
        Status = OrderStatus.Shipped,
        AnomalyReason = reason,
        TotalAmount = 10m,
        ExpectedDelivery = Today.AddDays(-daysPastExpected),
        LineItems = [new LineItem { Sku = "SKU-TEST", ProductName = "Test", Quantity = 1, UnitPrice = 10m }]
    };

    // ---- FR-001: classification belongs to the order, not the query ----

    [Fact]
    public void EachFilter_ReturnsANonEmptyResult()
    {
        foreach (var reason in Enum.GetValues<AnomalyReason>())
        {
            Assert.NotEmpty(Select(reason).Results);
        }
    }

    [Fact]
    public void TheThreeFilters_ReturnDisjointOrderSets()
    {
        var delayed = Select(AnomalyReason.Delayed).Ids;
        var missing = Select(AnomalyReason.Missing).Ids;
        var paymentFailed = Select(AnomalyReason.PaymentFailed).Ids;

        Assert.Empty(delayed.Intersect(missing));
        Assert.Empty(delayed.Intersect(paymentFailed));
        Assert.Empty(missing.Intersect(paymentFailed));
    }

    [Fact]
    public void AnomalyType_IsIdenticalWhetherFilteredOrNot()
    {
        var unfiltered = Select(null).Results.ToDictionary(r => r.OrderId, r => r.AnomalyType);

        foreach (var reason in Enum.GetValues<AnomalyReason>())
        {
            foreach (var result in Select(reason).Results)
            {
                Assert.Equal(unfiltered[result.OrderId], result.AnomalyType);
            }
        }
    }

    [Fact]
    public void UnfilteredResult_IsTheUnionOfTheFilteredResults()
    {
        var union = Enum.GetValues<AnomalyReason>()
            .SelectMany(r => Select(r).Ids)
            .OrderBy(id => id);

        Assert.Equal(union, Select(null).Ids.OrderBy(id => id));
    }

    [Fact]
    public void NonAnomalousOrders_AreNeverReturned()
    {
        var anomalous = Select(null).Ids;
        var normal = SeedOrders().Where(o => o.AnomalyReason is null).Select(o => o.OrderId);

        Assert.Empty(anomalous.Intersect(normal));
    }

    // ---- FR-002: the payload carries a join key and the contract fields ----

    [Fact]
    public void EveryResult_CarriesTheFullContractPayload()
    {
        foreach (var result in Select(null).Results)
        {
            Assert.False(string.IsNullOrWhiteSpace(result.CustomerId), $"{result.OrderId} has no customerId");
            Assert.True(result.TotalAmount > 0, $"{result.OrderId} has no totalAmount");
            Assert.NotEqual(default, result.ExpectedDelivery);
            Assert.NotEmpty(result.LineItems);
            Assert.All(result.LineItems, li => Assert.False(string.IsNullOrWhiteSpace(li.Sku)));
        }
    }

    [Fact]
    public void LineItemSkus_AllowCorrelationWithoutAPerOrderLookup()
    {
        var skus = Select(null).Results.SelectMany(r => r.LineItems).Select(li => li.Sku).ToArray();

        Assert.NotEmpty(skus);
        Assert.All(skus, sku => Assert.StartsWith("SKU-", sku));
    }

    // ---- FR-004: severity carries information ----

    [Theory]
    [InlineData(0, "medium")]
    [InlineData(7, "medium")]   // boundary: exactly at the threshold does not escalate
    [InlineData(8, "high")]     // boundary: one day past does
    [InlineData(60, "high")]
    public void DelayedSeverity_EscalatesPastTheThreshold(int daysPastExpected, string expected)
    {
        var anomaly = AnomalySelector.ToAnomaly(OrderWith(AnomalyReason.Delayed, daysPastExpected), Today);

        Assert.Equal(expected, anomaly.Severity);
    }

    [Theory]
    [InlineData(AnomalyReason.Missing)]
    [InlineData(AnomalyReason.PaymentFailed)]
    public void NonDelaySeverity_IsAlwaysHigh(AnomalyReason reason)
    {
        var anomaly = AnomalySelector.ToAnomaly(OrderWith(reason, 0), Today);

        Assert.Equal("high", anomaly.Severity);
    }

    // ---- FR-005: date-derived values are deterministic ----

    [Fact]
    public void DaysOverdue_IsCountedFromTheSuppliedDate()
    {
        var anomaly = AnomalySelector.ToAnomaly(OrderWith(AnomalyReason.Delayed, 12), Today);

        Assert.Equal(12, anomaly.DaysOverdue);
    }

    [Fact]
    public void DaysOverdue_IsNullForAnomaliesThatAreNotLate()
    {
        foreach (var reason in new[] { AnomalyReason.Missing, AnomalyReason.PaymentFailed })
        {
            Assert.Null(AnomalySelector.ToAnomaly(OrderWith(reason, 5), Today).DaysOverdue);
        }
    }

    [Fact]
    public void DaysOverdue_IsNeverNegative()
    {
        var notYetDue = OrderWith(AnomalyReason.Delayed, -10);

        Assert.Equal(0, AnomalySelector.ToAnomaly(notYetDue, Today).DaysOverdue);
    }

    // ---- Filter parsing ----

    [Theory]
    [InlineData("delayed", AnomalyReason.Delayed)]
    [InlineData("missing", AnomalyReason.Missing)]
    [InlineData("payment-failed", AnomalyReason.PaymentFailed)]
    [InlineData("DELAYED", AnomalyReason.Delayed)]
    [InlineData("  missing  ", AnomalyReason.Missing)]
    public void ParseReason_AcceptsTheDocumentedVocabulary(string input, AnomalyReason expected)
    {
        Assert.Equal(expected, AnomalySelector.ParseReason(input));
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("payment_failed")]
    [InlineData("lost")]
    [InlineData("")]
    public void ParseReason_RejectsAnythingElse(string input)
    {
        Assert.Null(AnomalySelector.ParseReason(input));
    }

    [Fact]
    public void ValidFilters_MatchesTheParseableVocabulary()
    {
        Assert.All(AnomalySelector.ValidFilters, f => Assert.NotNull(AnomalySelector.ParseReason(f)));
        Assert.Equal(Enum.GetValues<AnomalyReason>().Length, AnomalySelector.ValidFilters.Length);
    }
}
