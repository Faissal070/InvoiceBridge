using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderSync.Data;
using OrderSync.MockShop;
using OrderSync.Models;
using OrderSync.Options;

namespace OrderSync.Services;

/// <summary>
/// Lets demo visitors place orders in the fake webshop and retry failed ones, within limits that
/// keep the Moneybird sandbox from filling up.
/// </summary>
public class DemoOrderService(
    OrderSyncDbContext db,
    MockOrderStore store,
    DemoRateLimiter limiter,
    OrderInvoicingService invoicing,
    OrderChangeNotifier notifier,
    IOptions<DemoOptions> options,
    TimeProvider time,
    ILogger<DemoOrderService> logger)
{
    private const string OrderIdPrefix = "WS-";

    // One order at a time, so concurrent visitors cannot exceed the limits or pick the same order id.
    private static readonly SemaphoreSlim PlaceGate = new(1, 1);

    private readonly DemoOptions settings = options.Value;

    /// <summary>
    /// Places a random demo order in the webshop and registers it, so it can be invoiced right away.
    /// </summary>
    /// <param name="simulateOutage">The webshop fails the first request for this order.</param>
    public async Task<DemoOrderResult> PlaceOrderAsync(string visitorId, bool simulateOutage, CancellationToken cancellationToken = default)
    {
        if (!invoicing.IsConfigured)
        {
            return DemoOrderResult.Refused("Factureren staat op dit moment uit, dus er kunnen geen demobestellingen worden geplaatst.");
        }

        await PlaceGate.WaitAsync(cancellationToken);
        try
        {
            var now = time.GetUtcNow();
            if (await GlobalLimitReasonAsync(now, cancellationToken) is { } reason)
            {
                return DemoOrderResult.Refused(reason);
            }

            if (!limiter.TryAcquireOrder(visitorId, out var retryAfter))
            {
                return DemoOrderResult.Refused(
                    $"Je hebt net al een paar bestellingen geplaatst. Probeer het over {Describe(retryAfter)} opnieuw.");
            }

            var order = DemoOrderFactory.Create(await NextOrderIdAsync(cancellationToken), now, Random.Shared);
            if (simulateOutage)
            {
                store.SimulateOutage(order.OrderId);
            }
            store.TryAdd(order);

            var id = await RegisterAsync(order.OrderId, now, cancellationToken);
            logger.LogInformation("Demo order {OrderId} placed (outage: {Outage})", order.OrderId, simulateOutage);
            notifier.NotifyChanged();
            return DemoOrderResult.Placed(id, order);
        }
        finally
        {
            PlaceGate.Release();
        }
    }

    /// <summary>
    /// Manually retries a failed order, at most once per <see cref="DemoOptions.RetryCooldownSeconds"/> per visitor.
    /// </summary>
    public async Task<DemoRetryResult> RetryAsync(string visitorId, int syncedOrderId, CancellationToken cancellationToken = default)
    {
        if (!limiter.TryAcquireRetry(visitorId))
        {
            return new DemoRetryResult(false, "Even geduld: je kunt over een paar seconden opnieuw proberen.");
        }

        return new DemoRetryResult(await invoicing.RetryOrderAsync(syncedOrderId, cancellationToken), null);
    }

    private async Task<string?> GlobalLimitReasonAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var demoOrders = db.SyncedOrders.Where(o => o.IsDemo);
        if (await demoOrders.CountAsync(cancellationToken) >= settings.MaxOrdersTotal)
        {
            return "De demo heeft het maximale aantal testfacturen bereikt. Bekijk gerust de facturen die al gemaakt zijn.";
        }

        var startOfDay = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        if (await demoOrders.CountAsync(o => o.CreatedAt >= startOfDay, cancellationToken) >= settings.MaxOrdersPerDay)
        {
            return "De demo-limiet voor vandaag is bereikt. Bekijk gerust de facturen die al gemaakt zijn, of probeer het morgen opnieuw.";
        }

        return null;
    }

    /// <summary>
    /// Continues after the highest number known to the database or the webshop, so ids stay unique
    /// after a restart (when the webshop's in-memory demo orders are gone).
    /// </summary>
    private async Task<string> NextOrderIdAsync(CancellationToken cancellationToken)
    {
        var known = await db.SyncedOrders.Select(o => o.OrderId).ToListAsync(cancellationToken);
        var highest = known
            .Concat(store.GetAll().Select(o => o.OrderId))
            .Select(id => id.StartsWith(OrderIdPrefix, StringComparison.Ordinal) && long.TryParse(id[OrderIdPrefix.Length..], out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"{OrderIdPrefix}{highest + 1}";
    }

    private async Task<int> RegisterAsync(string orderId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        db.SyncedOrders.Add(new SyncedOrder { OrderId = orderId, IsDemo = true, CreatedAt = now });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The worker's poll registered it first; just mark it as a demo order.
            db.ChangeTracker.Clear();
            await db.SyncedOrders.Where(o => o.OrderId == orderId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.IsDemo, true), cancellationToken);
        }

        return await db.SyncedOrders.Where(o => o.OrderId == orderId).Select(o => o.Id).SingleAsync(cancellationToken);
    }

    private static string Describe(TimeSpan wait) =>
        wait.TotalMinutes >= 1.5 ? $"ongeveer {Math.Ceiling(wait.TotalMinutes)} minuten" : "een minuutje";
}
