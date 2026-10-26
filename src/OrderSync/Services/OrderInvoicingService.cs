using System.Linq.Expressions;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderSync.Clients;
using OrderSync.Data;
using OrderSync.Exceptions;
using OrderSync.Models;
using OrderSync.Options;

namespace OrderSync.Services;

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
    OrderChangeNotifier notifier,
    TimeProvider time,
    IOptions<MoneybirdOptions> moneybirdOptions,
    SandboxSimulationService simulation,
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

        // Count invoices sent to Moneybird this calendar month (simulated ones don't count).
        var firstOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var syncedThisMonth = await db.SyncedOrders
            .CountAsync(o => o.Status == SyncStatus.Synced && o.SyncedAt >= firstOfMonth, cancellationToken);

        var limit = moneybirdOptions.Value.SandboxInvoiceLimit;
        if (syncedThisMonth >= limit)
            simulation.ActivateLocalLimit();
        else if (simulation.Reason == SimulationReason.LocalLimit)
            simulation.Deactivate(); // new calendar month — counter reset

        // When a previous 402 has been waiting for an hour, deactivate simulation so the next
        // order is tried against real Moneybird. If it gets 402 again, InvoiceAsync re-activates.
        if (simulation.ShouldRetryMoneybird)
        {
            logger.LogInformation("Sandbox-simulatie: een uur verstreken, probeer Moneybird opnieuw.");
            simulation.Deactivate();
        }

        var ids = await db.SyncedOrders
            .Where(IsEligible(force: false, time.GetUtcNow()))
            .OrderBy(o => o.Id)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        int synced = 0, failed = 0, simulated = 0;
        foreach (var id in ids)
        {
            var outcome = await InvoiceAsync(id, force: false, cancellationToken);
            if (outcome == Outcome.Synced) synced++;
            else if (outcome == Outcome.Simulated) simulated++;
            else if (outcome is Outcome.Failed or Outcome.RateLimited) failed++;
            if (outcome == Outcome.RateLimited)
            {
                logger.LogWarning("Moneybird rate limit reached; stopping this run");
                return new InvoiceRunResult(synced, failed, RateLimited: true, Simulated: simulated);
            }
        }

        if (ids.Count > 0)
        {
            logger.LogInformation("Invoicing run: {Synced} synced, {Failed} failed, {Simulated} simulated", synced, failed, simulated);
        }
        return new InvoiceRunResult(synced, failed, RateLimited: false, Simulated: simulated);
    }

    /// <summary>
    /// Fills in the invoice snapshot for orders that were synced before snapshots existed.
    /// Only reads from the webshop; Moneybird is not called.
    /// </summary>
    /// <returns>The number of orders that got a snapshot.</returns>
    public async Task<int> BackfillSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        var missing = await db.SyncedOrders
            .Where(o => o.Status == SyncStatus.Synced && o.MoneybirdInvoiceId != null && o.CustomerName == null)
            .Select(o => new { o.Id, o.OrderId, o.MoneybirdInvoiceId })
            .ToListAsync(cancellationToken);

        var filled = 0;
        foreach (var order in missing)
        {
            if (await shop.GetOrderAsync(order.OrderId, cancellationToken) is { } shopOrder)
            {
                await SaveSnapshotAsync(order.Id, order.MoneybirdInvoiceId, shopOrder, syncedAt: null);
                filled++;
            }
        }

        if (filled > 0)
        {
            logger.LogInformation("Added invoice snapshots for {Count} earlier synced orders", filled);
            notifier.NotifyChanged();
        }
        return filled;
    }

    /// <summary>
    /// Manually retries one order (from the dashboard), also after the automatic attempts ran out.
    /// </summary>
    /// <returns>True if the order is now synced or simulated.</returns>
    public async Task<bool> RetryOrderAsync(int syncedOrderId, CancellationToken cancellationToken = default)
    {
        var outcome = await InvoiceAsync(syncedOrderId, force: true, cancellationToken);
        return outcome is Outcome.Synced or Outcome.Simulated;
    }

    /// <summary>
    /// Invoices one order right away (e.g. a demo order), with the same rules as the automatic run.
    /// </summary>
    /// <returns>True if the order is now synced or simulated.</returns>
    public async Task<bool> InvoiceOrderAsync(int syncedOrderId, CancellationToken cancellationToken = default)
    {
        var outcome = await InvoiceAsync(syncedOrderId, force: false, cancellationToken);
        return outcome is Outcome.Synced or Outcome.Simulated;
    }

    private enum Outcome { Skipped, Synced, Simulated, Failed, RateLimited }

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
        notifier.NotifyChanged();

        var orderId = await db.SyncedOrders.Where(o => o.Id == id).Select(o => o.OrderId).SingleAsync(cancellationToken);
        try
        {
            // Always fetched — also for simulation — so the snapshot is available on the invoice page.
            // Validation failures (order not found, etc.) always result in Failed, even in simulation mode.
            var order = await shop.GetOrderAsync(orderId, cancellationToken)
                ?? throw new OrderNotFoundException(orderId);

            if (simulation.IsSimulating)
            {
                await MarkSimulatedAsync(id, order);
                logger.LogInformation("Order {OrderId} simulated (sandbox limit active)", orderId);
                return Outcome.Simulated;
            }

            string invoiceId;
            try
            {
                invoiceId = await CreateOrAdoptInvoiceAsync(order, cancellationToken);
            }
            catch (MoneybirdException ex) when (ex.IsPaymentRequired)
            {
                simulation.ActivatePaymentRequired();
                await MarkSimulatedAsync(id, order);
                logger.LogWarning("Order {OrderId} simulated after Moneybird 402", orderId);
                return Outcome.Simulated;
            }

            await MarkSyncedAsync(id, invoiceId, order);
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
            // The technical details only go to the log; the dashboard shows a reason anyone understands.
            var reason = FriendlyReason(ex);
            logger.LogWarning(ex, "Invoicing order {OrderId} failed: {Reason}", orderId, reason);
            await MarkFailedAsync(id, reason, cancellationToken);
            return ex is MoneybirdException { IsRateLimited: true } ? Outcome.RateLimited : Outcome.Failed;
        }
        finally
        {
            notifier.NotifyChanged();
        }
    }

    private static string FriendlyReason(Exception ex) => ex switch
    {
        MoneybirdException { IsRateLimited: true } => "Moneybird is even druk. OrderSync probeert het straks opnieuw.",
        MoneybirdException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } =>
            "OrderSync heeft geen toegang tot Moneybird. Controleer de koppeling.",
        MoneybirdException { StatusCode: HttpStatusCode.NotFound } =>
            "De Moneybird-administratie is niet gevonden. Controleer de koppeling.",
        MoneybirdException { StatusCode: HttpStatusCode.UnprocessableEntity } => "Moneybird heeft de factuurgegevens geweigerd.",
        MoneybirdException { StatusCode: null } => "Moneybird was even niet bereikbaar.",
        MoneybirdException => "Moneybird gaf een onverwachte fout.",
        HttpRequestException => "De webshop was even niet bereikbaar.",
        OrderNotFoundException => ex.Message,
        _ => "Er ging iets onverwachts mis."
    };

    private async Task<string> CreateOrAdoptInvoiceAsync(MockOrder order, CancellationToken cancellationToken)
    {
        var existing = await moneybird.FindInvoiceByReferenceAsync(order.OrderId, cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation("Order {OrderId} already has Moneybird invoice {InvoiceId}; adopting it", order.OrderId, existing.Id);
            return existing.Id;
        }

        var contact = await moneybird.FindContactByEmailAsync(order.Email, cancellationToken)
            ?? await moneybird.CreateContactAsync(ToContact(order), cancellationToken);

        var invoice = await moneybird.CreateInvoiceAsync(new NewMoneybirdInvoice(
            contact.Id,
            order.OrderId,
            InvoiceDateFor(order),
            order.Lines.Select(l => new NewMoneybirdInvoiceLine(l.Product, l.Quantity, l.UnitPrice)).ToList()),
            cancellationToken);

        return invoice.Id;
    }

    private Task MarkSyncedAsync(int id, string invoiceId, MockOrder order) =>
        SaveSnapshotAsync(id, invoiceId, order, syncedAt: time.GetUtcNow(), SyncStatus.Synced);

    private Task MarkSimulatedAsync(int id, MockOrder order) =>
        SaveSnapshotAsync(id, invoiceId: null, order, syncedAt: time.GetUtcNow(), SyncStatus.Simulated);

    /// <param name="syncedAt">New sync time, or null to keep the stored one (backfill).</param>
    private async Task SaveSnapshotAsync(int id, string? invoiceId, MockOrder order, DateTimeOffset? syncedAt, SyncStatus status = SyncStatus.Synced)
    {
        // Not cancellable: once Moneybird has the invoice (or we decided to simulate) we want to record it.
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        await db.SyncedOrders.Where(o => o.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(o => o.Status, status)
            .SetProperty(o => o.MoneybirdInvoiceId, invoiceId)
            .SetProperty(o => o.SyncedAt, o => syncedAt ?? o.SyncedAt)
            .SetProperty(o => o.LastError, (string?)null)
            .SetProperty(o => o.CustomerName, order.CustomerName)
            .SetProperty(o => o.InvoiceDate, InvoiceDateFor(order))
            .SetProperty(o => o.Total, order.Total), CancellationToken.None);

        await db.InvoiceLines.Where(l => l.SyncedOrderId == id).ExecuteDeleteAsync(CancellationToken.None);
        db.InvoiceLines.AddRange(order.Lines.Select(l => new InvoiceLine
        {
            SyncedOrderId = id,
            Description = l.Product,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice
        }));
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
    }

    private static DateOnly InvoiceDateFor(MockOrder order) => DateOnly.FromDateTime(order.OrderDate.Date);

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
