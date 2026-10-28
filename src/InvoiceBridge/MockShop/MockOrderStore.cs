using InvoiceBridge.Models;

namespace InvoiceBridge.MockShop;

/// <summary>
/// In-memory order book of the fake webshop: the seed orders plus demo orders placed while the app runs.
/// </summary>
/// <remarks>
/// Demo orders live in memory only. Once invoiced, InvoiceBridge keeps its own snapshot of the invoice,
/// so a restart of the (fake) webshop does not affect what visitors can see.
/// </remarks>
public class MockOrderStore
{
    private readonly Lock gate = new();
    private readonly List<MockOrder> orders = [.. MockOrderSeed.Orders];
    private readonly HashSet<string> outages = [];

    public IReadOnlyList<MockOrder> GetAll()
    {
        lock (gate)
        {
            return orders.OrderBy(o => o.OrderDate).ToList();
        }
    }

    public MockOrder? Find(string orderId)
    {
        lock (gate)
        {
            return orders.FirstOrDefault(o => o.OrderId == orderId);
        }
    }

    /// <summary>Adds an order; returns false if an order with the same id already exists.</summary>
    public bool TryAdd(MockOrder order)
    {
        lock (gate)
        {
            if (orders.Any(o => o.OrderId == order.OrderId))
            {
                return false;
            }
            orders.Add(order);
            return true;
        }
    }

    /// <summary>Makes the next request for this order fail, as if the webshop is briefly down.</summary>
    public void SimulateOutage(string orderId)
    {
        lock (gate)
        {
            outages.Add(orderId);
        }
    }

    /// <summary>True (once) if a simulated outage is pending for this order.</summary>
    public bool TryConsumeOutage(string orderId)
    {
        lock (gate)
        {
            return outages.Remove(orderId);
        }
    }
}
