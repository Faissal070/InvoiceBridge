using OrderSync.MockShop;

namespace OrderSync.Tests.MockShop;

public class DemoOrderFactoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Creates_fake_order_from_the_seed_assortment()
    {
        var products = MockOrderSeed.Orders.SelectMany(o => o.Lines).DistinctBy(l => l.Product).ToDictionary(l => l.Product, l => l.UnitPrice, StringComparer.Ordinal);
        var random = new Random(42);

        for (var i = 0; i < 50; i++)
        {
            var order = DemoOrderFactory.Create("WS-20261021", Now, random);

            Assert.Equal("WS-20261021", order.OrderId);
            Assert.Equal(Now, order.OrderDate);
            Assert.Contains(' ', order.CustomerName);
            Assert.EndsWith("@example.nl", order.Email);
            Assert.DoesNotContain(' ', order.Email);
            Assert.InRange(order.Lines.Count, 1, 3);
            Assert.Equal(order.Lines.Count, order.Lines.Select(l => l.Product).Distinct().Count());
            Assert.All(order.Lines, l =>
            {
                Assert.Equal(products[l.Product], l.UnitPrice);
                Assert.InRange(l.Quantity, 1, 3);
            });
        }
    }
}
