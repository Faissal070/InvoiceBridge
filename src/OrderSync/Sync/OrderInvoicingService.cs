using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using OrderSync.Data;
using OrderSync.MockShop;
using OrderSync.Moneybird;

namespace OrderSync.Sync;

public record InvoiceRunResult(int Synced, int Failed, bool RateLimited, bool NotConfigured = false);

/// <summary>
/// Turns registered orders into Moneybird invoices, guaranteeing at most one invoice per order.
/// </summary>
/// <remarks>
/// Duplicate protection works in three layers:
/// 1. An order is claimed with a conditional update (-> Processing) before Moneybird is called,
///    so two runs can never work on the same order at the same time.
/// 2. Before creating an invoice, Moneybird is searched for an invoice with the order id as
///    reference. If one exists (e.g. after a crash or timeout), it is adopted instead.
/// 3. If that search fails, nothing is created and the order is marked Failed.
/// </remarks>
public class OrderInvoicingService(
    OrderSyncDbContext db,
    IMoneybirdClient moneybird,
    MockShopClient shop,
    TimeProvider time,
    ILogger<OrderInvoicingService> logger)
{
    public const int MaxAutomaticAttempts = 5;

    public static readonly TimeSpan StaleProcessingAfter = TimeSpan.FromMinutes(10);

    public bool IsConfigured => moneybird.IsConfigured;

    /// <summary>
    /// Invoices all orders that are pending, failed with attempts left, or stuck in Processing.
    /// </summary>
    public async Task<InvoiceRunResult> InvoicePendingOrdersAsync(CancellationToken cancellationToken = default)
    {
        if (!moneybird.IsConfigured)
        {
            logger.LogWarning("Moneybird is not configured; skipping invoicing");
            return new InvoiceRunResult(0, 0, false, NotConfigured: true);
        }

        var ids = await db.SyncedOrders
            .Where(IsEligible(force: false, time.GetUtcNow()))
            .OrderBy(o => o.Id)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        int synced = 0, failed = 0;
        foreach (var id in ids)
        {
            var outcome = await InvoiceAsync(id, force: false, cancellationToken);
            if (outcome == Outcome.Synced) synced++;
            if (outcome is Outcome.Failed or Outcome.RateLimited) failed++;
            if (outcome == Outcome.RateLimited)
            {
                logger.LogWarning("Moneybird rate limit reached; stopping this run");
                return new InvoiceRunResult(synced, failed, RateLimited: true);
            }
        }

        if (ids.Count > 0)
        {
            logger.LogInformation("Invoicing run: {Synced} synced, {Failed} failed", synced, failed);
        }
        return new InvoiceRunResult(synced, failed, RateLimited: false);
    }

    /// <summary>
    /// Manually retries one order (from the dashboard), also after the automatic attempts ran out.
    /// </summary>
    /// <returns>True if the order is now synced.</returns>
    public async Task<bool> RetryOrderAsync(int syncedOrderId, CancellationToken cancellationToken = default) =>
        await InvoiceAsync(syncedOrderId, force: true, cancellationToken) == Outcome.Synced;

    private enum Outcome { Skipped, Synced, Failed, RateLimited }

    private async Task<Outcome> InvoiceAsync(int id, bool force, CancellationToken cancellationToken)
    {
        if (!moneybird.IsConfigured)
        {
            return Outcome.Skipped;
        }

        var now = time.GetUtcNow();
        var claimed = await db.SyncedOrders
            .Where(o => o.Id == id)
            .Where(IsEligible(force, now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, SyncStatus.Processing)
                .SetProperty(o => o.Attempts, o => o.Attempts + 1)
                .SetProperty(o => o.LastAttemptAt, now), cancellationToken);

        if (claimed == 0)
        {
            // Already synced, or claimed by another run.
            return Outcome.Skipped;
        }

        var orderId = await db.SyncedOrders.Where(o => o.Id == id).Select(o => o.OrderId).SingleAsync(cancellationToken);
        try
        {
            var invoiceId = await CreateOrAdoptInvoiceAsync(orderId, cancellationToken);
            await MarkSyncedAsync(id, invoiceId, cancellationToken);
            logger.LogInformation("Order {OrderId} invoiced as Moneybird invoice {InvoiceId}", orderId, invoiceId);
            return Outcome.Synced;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Left in Processing on purpose: the stale check plus the reference lookup make it safe to resume.
            throw;
        }
        catch (Exception ex)
        {
            var reason = ex switch
            {
                MoneybirdException => ex.Message,
                HttpRequestException => $"Webshop niet bereikbaar: {ex.Message}",
                _ => $"Onverwachte fout: {ex.Message}"
            };
            logger.LogWarning(ex, "Invoicing order {OrderId} failed: {Reason}", orderId, reason);
            await MarkFailedAsync(id, reason, cancellationToken);
            return ex is MoneybirdException { IsRateLimited: true } ? Outcome.RateLimited : Outcome.Failed;
        }
    }

    private async Task<string> CreateOrAdoptInvoiceAsync(string orderId, CancellationToken cancellationToken)
    {
        var existing = await moneybird.FindInvoiceByReferenceAsync(orderId, cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation("Order {OrderId} already has Moneybird invoice {InvoiceId}; adopting it", orderId, existing.Id);
            return existing.Id;
        }

        var order = await shop.GetOrderAsync(orderId, cancellationToken)
            ?? throw new InvalidOperationException($"Bestelling {orderId} niet gevonden in de webshop.");

        var contact = await moneybird.FindContactByEmailAsync(order.Email, cancellationToken)
            ?? await moneybird.CreateContactAsync(ToContact(order), cancellationToken);

        var invoice = await moneybird.CreateInvoiceAsync(new NewMoneybirdInvoice(
            contact.Id,
            order.OrderId,
            DateOnly.FromDateTime(order.OrderDate.Date),
            order.Lines.Select(l => new NewMoneybirdInvoiceLine(l.Product, l.Quantity, l.UnitPrice)).ToList()),
            cancellationToken);

        return invoice.Id;
    }

    private Task MarkSyncedAsync(int id, string invoiceId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        // Not cancellable: once Moneybird has the invoice we want to record it.
        return db.SyncedOrders.Where(o => o.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(o => o.Status, SyncStatus.Synced)
            .SetProperty(o => o.MoneybirdInvoiceId, invoiceId)
            .SetProperty(o => o.SyncedAt, now)
            .SetProperty(o => o.LastError, (string?)null), CancellationToken.None);
    }

    private Task MarkFailedAsync(int id, string reason, CancellationToken cancellationToken) =>
        db.SyncedOrders.Where(o => o.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(o => o.Status, SyncStatus.Failed)
            .SetProperty(o => o.LastError, reason), cancellationToken);

    private static Expression<Func<SyncedOrder, bool>> IsEligible(bool force, DateTimeOffset now)
    {
        var staleBefore = now - StaleProcessingAfter;
        return o =>
            o.Status == SyncStatus.Pending
            || (o.Status == SyncStatus.Failed && (force || o.Attempts < MaxAutomaticAttempts))
            || (o.Status == SyncStatus.Processing && o.LastAttemptAt < staleBefore);
    }

    private static NewMoneybirdContact ToContact(MockOrder order)
    {
        var parts = order.CustomerName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return new NewMoneybirdContact(parts[0], parts.Length > 1 ? parts[1] : "", order.Email);
    }
}
