using OrderSync.MockShop;
using OrderSync.Models;

namespace OrderSync.Tests.MockShop;

public class MockOrderSeedTests
{
    [Fact]
    public void Seed_contains_20_orders_with_unique_ids()
    {
        var orders = MockOrderSeed.Orders;

        Assert.Equal(20, orders.Count);
        Assert.Equal(orders.Count, orders.Select(o => o.OrderId).Distinct().Count());
    }

    [Fact]
    public void Every_order_has_customer_details_and_valid_lines()
    {
        Assert.All(MockOrderSeed.Orders, order =>
        {
            Assert.False(string.IsNullOrWhiteSpace(order.CustomerName));
            Assert.EndsWith("@example.nl", order.Email);
            Assert.NotEmpty(order.Lines);
            Assert.All(order.Lines, line =>
            {
                Assert.False(string.IsNullOrWhiteSpace(line.Product));
                Assert.True(line.Quantity > 0);
                Assert.True(line.UnitPrice > 0);
            });
        });
    }

    [Fact]
    public void Totals_are_sum_of_quantity_times_unit_price()
    {
        var order = new MockOrder("T-1", "Test", "test@example.nl", DateTimeOffset.UtcNow,
        [
            new MockOrderLine("A", 2, 3.50m),
            new MockOrderLine("B", 1, 10.00m)
        ]);

        Assert.Equal(7.00m, order.Lines[0].LineTotal);
        Assert.Equal(17.00m, order.Total);
    }
}
