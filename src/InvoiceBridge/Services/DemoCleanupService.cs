using Microsoft.EntityFrameworkCore;
using InvoiceBridge.Clients;
using InvoiceBridge.Data;
using InvoiceBridge.Models;

namespace InvoiceBridge.Services;

/// <summary>
/// Removes unfinished demo orders (failed, pending or stuck) so the dashboard looks clean, without touching Moneybird.
/// An order is only removed when Moneybird has no invoice with its reference: demo order ids are reused after
/// removal, and a leftover invoice would otherwise be adopted by the next demo order.
/// </summary>
public sealed class DemoCleanupService(
    InvoiceBridgeDbContext db,
    IMoneybirdClient moneybird,
    ILogger<DemoCleanupService> logger)
{
    public async Task<DemoCleanupResult> RemoveUnfinishedDemoOrdersAsync(CancellationToken cancellationToken = default)
    {
        if (!moneybird.IsConfigured)
            throw new InvalidOperationException("Moneybird is not configured; cannot verify that removed orders have no invoice.");

        var unfinished = await db.SyncedOrders
            .Where(o => o.IsDemo && o.Status != SyncStatus.Synced && o.Status != SyncStatus.Simulated)
            .ToListAsync(cancellationToken);

        var removed = new List<string>();
        var kept = new List<string>();
        foreach (var order in unfinished)
        {
            if (await moneybird.FindInvoiceByReferenceAsync(order.OrderId, cancellationToken) is not null)
            {
                logger.LogWarning("Keeping {OrderId}: Moneybird already has an invoice for it", order.OrderId);
                kept.Add(order.OrderId);
                continue;
            }

            db.SyncedOrders.Remove(order);
            removed.Add(order.OrderId);
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Demo cleanup removed {Count} unfinished demo orders", removed.Count);
        return new DemoCleanupResult(removed, kept);
    }
}
