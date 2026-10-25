using Microsoft.EntityFrameworkCore;
using OrderSync.Data;

namespace OrderSync.Sync;

public class OrderSyncService(MockShopClient shop, OrderSyncDbContext db, ILogger<OrderSyncService> logger)
{
    /// <summary>
    /// Fetches orders from the webshop and registers the ones we have not seen yet as Pending.
    /// </summary>
    /// <returns>The number of newly registered orders.</returns>
    public async Task<int> SyncNewOrdersAsync(CancellationToken cancellationToken = default)
    {
        var orders = await shop.GetOrdersAsync(cancellationToken);

        var knownIds = await db.SyncedOrders
            .Select(o => o.OrderId)
            .ToHashSetAsync(cancellationToken);

        var newOrders = orders
            .Where(o => !knownIds.Contains(o.OrderId))
            .Select(o => new SyncedOrder { OrderId = o.OrderId })
            .ToList();

        // TODO: create Moneybird invoices for pending orders once the client exists.

        if (newOrders.Count > 0)
        {
            db.SyncedOrders.AddRange(newOrders);
            await db.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation("Fetched {Total} orders from shop, {New} new", orders.Count, newOrders.Count);
        return newOrders.Count;
    }
}
