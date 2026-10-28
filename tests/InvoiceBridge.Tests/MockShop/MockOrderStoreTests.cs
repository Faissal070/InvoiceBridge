using InvoiceBridge.MockShop;
using InvoiceBridge.Models;

namespace InvoiceBridge.Tests.MockShop;

public class MockOrderStoreTests
{
    private readonly MockOrderStore store = new();

    [Fact]
    public void Starts_with_the_seed_orders()
    {
        Assert.Equal(MockOrderSeed.Orders.Select(o => o.OrderId).Order(), store.GetAll().Select(o => o.OrderId).Order());
    }

    [Fact]
    public void Added_order_can_be_found_and_duplicates_are_rejected()
    {
        var order = Order("WS-20261021");

        Assert.True(store.TryAdd(order));
        Assert.False(store.TryAdd(Order("WS-20261021")));

        Assert.Same(order, store.Find("WS-20261021"));
        Assert.Equal(21, store.GetAll().Count);
    }

    [Fact]
    public void Simulated_outage_is_consumed_once()
    {
        store.SimulateOutage("WS-20261001");

        Assert.True(store.TryConsumeOutage("WS-20261001"));
        Assert.False(store.TryConsumeOutage("WS-20261001"));
        Assert.False(store.TryConsumeOutage("WS-20261002"));
    }

    private static MockOrder Order(string id) =>
        new(id, "Test Klant", "test@example.nl", DateTimeOffset.UtcNow, [new MockOrderLine("Theemok aardewerk", 1, 12.95m)]);
}
