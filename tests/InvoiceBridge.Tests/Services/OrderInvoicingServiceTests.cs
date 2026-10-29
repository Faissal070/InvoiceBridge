using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using InvoiceBridge.Clients;
using InvoiceBridge.Data;
using InvoiceBridge.Exceptions;
using InvoiceBridge.Models;
using InvoiceBridge.Options;
using InvoiceBridge.Services;

namespace InvoiceBridge.Tests.Services;

public sealed class OrderInvoicingServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly DbContextOptions<InvoiceBridgeDbContext> dbOptions;
    private readonly FakeMoneybird moneybird = new();
    private readonly ShopHandler shop = new();
    private readonly FixedTime time = new(Now);

    public OrderInvoicingServiceTests()
    {
        connection.Open();
        dbOptions = new DbContextOptionsBuilder<InvoiceBridgeDbContext>().UseSqlite(connection).Options;
        using var db = new InvoiceBridgeDbContext(dbOptions);
        db.Database.EnsureCreated();
    }

    public void Dispose() => connection.Dispose();

    [Fact]
    public async Task Pending_order_becomes_synced_with_invoice_id()
    {
        await AddOrderAsync("WS-1");

        var result = await RunAsync();

        Assert.Equal(new InvoiceRunResult(1, 0, false), result);
        var order = await GetOrderAsync("WS-1");
        Assert.Equal(SyncStatus.Synced, order.Status);
        Assert.Equal("inv-1", order.MoneybirdInvoiceId);
        Assert.Equal(Now, order.SyncedAt);
        Assert.Equal(1, order.Attempts);
        Assert.Null(order.LastError);

        var invoice = Assert.Single(moneybird.CreatedInvoices);
        Assert.Equal("WS-1", invoice.Reference);
        Assert.Equal(new DateOnly(2026, 10, 1), invoice.InvoiceDate);
        Assert.Equal([new NewMoneybirdInvoiceLine("Koffiebonen", 2, 12.50m)], invoice.Lines);
    }

    [Fact]
    public async Task Synced_order_keeps_a_snapshot_of_the_invoice()
    {
        await AddOrderAsync("WS-1", customer: "Levi van Leeuwen",
            lines: [new MockOrderLine("Koffiebonen", 2, 12.50m), new MockOrderLine("Theemok", 1, 9.95m)]);

        await RunAsync();

        var order = await GetOrderAsync("WS-1");
        Assert.Equal("Levi van Leeuwen", order.CustomerName);
        Assert.Equal(new DateOnly(2026, 10, 1), order.InvoiceDate);
        Assert.Equal(34.95m, order.Total);
        Assert.Equal(
            [("Koffiebonen", 2, 12.50m), ("Theemok", 1, 9.95m)],
            order.InvoiceLines.OrderBy(l => l.Id).Select(l => (l.Description, l.Quantity, l.UnitPrice)));
    }

    [Fact]
    public async Task Adopted_invoice_also_gets_a_snapshot()
    {
        await AddOrderAsync("WS-1");
        moneybird.Invoices.Add(new MoneybirdInvoice("inv-earlier", "WS-1"));

        await RunAsync();

        var order = await GetOrderAsync("WS-1");
        Assert.Equal(25.00m, order.Total);
        Assert.Single(order.InvoiceLines);
    }

    [Fact]
    public async Task Backfill_adds_snapshot_to_earlier_synced_orders_without_calling_moneybird()
    {
        var earlier = Now.AddDays(-1);
        await AddOrderAsync("WS-1", SyncStatus.Synced);
        using (var db = new InvoiceBridgeDbContext(dbOptions))
        {
            await db.SyncedOrders.ExecuteUpdateAsync(s => s
                .SetProperty(o => o.MoneybirdInvoiceId, "inv-old")
                .SetProperty(o => o.SyncedAt, earlier));
        }

        Assert.Equal(1, await CreateService().BackfillSnapshotsAsync());
        Assert.Equal(0, await CreateService().BackfillSnapshotsAsync());

        var order = await GetOrderAsync("WS-1");
        Assert.Equal("Sanne de Vries", order.CustomerName);
        Assert.Equal(25.00m, order.Total);
        Assert.Single(order.InvoiceLines);
        Assert.Equal("inv-old", order.MoneybirdInvoiceId);
        Assert.Equal(earlier, order.SyncedAt);
        Assert.Equal(0, moneybird.FindInvoiceCalls);
    }

    [Fact]
    public async Task Invoice_order_invoices_only_that_order()
    {
        await AddOrderAsync("WS-1");
        await AddOrderAsync("WS-2");

        Assert.True(await CreateService().InvoiceOrderAsync((await GetOrderAsync("WS-2")).Id));

        Assert.Equal(SyncStatus.Pending, (await GetOrderAsync("WS-1")).Status);
        Assert.Equal(SyncStatus.Synced, (await GetOrderAsync("WS-2")).Status);
        Assert.False(await CreateService().InvoiceOrderAsync((await GetOrderAsync("WS-2")).Id));
        Assert.Single(moneybird.CreatedInvoices);
    }

    [Fact]
    public async Task New_contact_is_created_with_split_name()
    {
        await AddOrderAsync("WS-1", customer: "Levi van Leeuwen");

        await RunAsync();

        var contact = Assert.Single(moneybird.CreatedContacts);
        Assert.Equal(new NewMoneybirdContact("Levi", "van Leeuwen", "klant-WS-1@example.nl"), contact);
    }

    [Fact]
    public async Task Existing_contact_is_reused()
    {
        await AddOrderAsync("WS-1");
        moneybird.Contacts.Add(new MoneybirdContact("c-existing", MoneybirdClient.CustomerIdFor("klant-WS-1@example.nl")));

        await RunAsync();

        Assert.Empty(moneybird.CreatedContacts);
        Assert.Equal("c-existing", Assert.Single(moneybird.CreatedInvoices).ContactId);
    }

    [Fact]
    public async Task Existing_invoice_with_same_reference_is_adopted_not_duplicated()
    {
        await AddOrderAsync("WS-1");
        moneybird.Invoices.Add(new MoneybirdInvoice("inv-earlier", "WS-1"));

        await RunAsync();

        Assert.Empty(moneybird.CreatedInvoices);
        var order = await GetOrderAsync("WS-1");
        Assert.Equal(SyncStatus.Synced, order.Status);
        Assert.Equal("inv-earlier", order.MoneybirdInvoiceId);
    }

    [Fact]
    public async Task Synced_order_is_never_invoiced_again()
    {
        await AddOrderAsync("WS-1");
        await RunAsync();

        // Even if the remote lookup would not find it anymore, the local state prevents a second invoice.
        moneybird.Invoices.Clear();
        await RunAsync();
        await CreateService().RetryOrderAsync((await GetOrderAsync("WS-1")).Id);

        Assert.Single(moneybird.CreatedInvoices);
    }

    [Fact]
    public async Task Concurrent_runs_create_only_one_invoice()
    {
        await AddOrderAsync("WS-1");
        var id = (await GetOrderAsync("WS-1")).Id;
        InvoiceRunResult? second = null;
        bool? retried = null;
        moneybird.BeforeFindInvoice = async () =>
        {
            // While the first run is talking to Moneybird, a second run and a manual retry start.
            moneybird.BeforeFindInvoice = null;
            second = await RunAsync();
            retried = await CreateService().RetryOrderAsync(id);
        };

        var first = await RunAsync();

        Assert.Single(moneybird.CreatedInvoices);
        Assert.Equal(1, first.Synced);
        Assert.Equal(0, second!.Synced);
        Assert.False(retried);
        Assert.Equal(1, (await GetOrderAsync("WS-1")).Attempts);
    }

    [Fact]
    public async Task Moneybird_error_marks_order_failed_with_reason()
    {
        await AddOrderAsync("WS-1");
        moneybird.CreateInvoiceError = new MoneybirdException("Moneybird weigerde de gegevens (422): prijs ongeldig", HttpStatusCode.UnprocessableEntity);

        var result = await RunAsync();

        Assert.Equal(new InvoiceRunResult(0, 1, false), result);
        var order = await GetOrderAsync("WS-1");
        Assert.Equal(SyncStatus.Failed, order.Status);
        Assert.Equal("Moneybird heeft de factuurgegevens geweigerd.", order.LastError);
        Assert.Equal(1, order.Attempts);
        Assert.Null(order.MoneybirdInvoiceId);
    }

    [Fact]
    public async Task Webshop_outage_fails_with_friendly_reason_and_retry_succeeds()
    {
        await AddOrderAsync("WS-1");
        shop.Unavailable = true;

        await RunAsync();

        var failed = await GetOrderAsync("WS-1");
        Assert.Equal(SyncStatus.Failed, failed.Status);
        Assert.Equal("De webshop was even niet bereikbaar.", failed.LastError);
        Assert.Equal(0, moneybird.FindInvoiceCalls);

        shop.Unavailable = false;
        Assert.True(await CreateService().RetryOrderAsync(failed.Id));

        var synced = await GetOrderAsync("WS-1");
        Assert.Equal(SyncStatus.Synced, synced.Status);
        Assert.Equal(2, synced.Attempts);
        Assert.Single(moneybird.CreatedInvoices);
    }

    [Fact]
    public async Task Status_changes_are_announced()
    {
        await AddOrderAsync("WS-1");
        var notifier = new OrderChangeNotifier();
        var changes = 0;
        notifier.Changed += () => changes++;

        await CreateService(notifier).InvoicePendingOrdersAsync();

        // Once when claimed (Processing), once when done (Synced).
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task Failed_lookup_creates_nothing()
    {
        await AddOrderAsync("WS-1");
        moneybird.FindInvoiceError = new MoneybirdException("Moneybird is niet bereikbaar: timeout");

        await RunAsync();

        Assert.Empty(moneybird.CreatedInvoices);
        Assert.Equal(SyncStatus.Failed, (await GetOrderAsync("WS-1")).Status);
    }

    [Fact]
    public async Task Failed_order_is_retried_and_clears_error_on_success()
    {
        await AddOrderAsync("WS-1");
        moneybird.CreateInvoiceError = new MoneybirdException("tijdelijke fout");
        await RunAsync();

        moneybird.CreateInvoiceError = null;
        time.Advance(OrderInvoicingService.RetryDelayAfter(1));
        await RunAsync();

        var order = await GetOrderAsync("WS-1");
        Assert.Equal(SyncStatus.Synced, order.Status);
        Assert.Equal(2, order.Attempts);
        Assert.Null(order.LastError);
    }

    [Fact]
    public async Task Failed_order_waits_for_its_backoff_before_the_next_automatic_retry()
    {
        await AddOrderAsync("WS-1");
        moneybird.CreateInvoiceError = new MoneybirdException("tijdelijke fout");
        await RunAsync();
        await RunAsync();
        Assert.Equal(1, (await GetOrderAsync("WS-1")).Attempts);

        time.Advance(OrderInvoicingService.RetryDelayAfter(1));
        await RunAsync();
        Assert.Equal(2, (await GetOrderAsync("WS-1")).Attempts);

        // The second delay is longer than the first.
        time.Advance(OrderInvoicingService.RetryDelayAfter(1));
        await RunAsync();
        Assert.Equal(2, (await GetOrderAsync("WS-1")).Attempts);

        time.Advance(OrderInvoicingService.RetryDelayAfter(2));
        await RunAsync();
        Assert.Equal(3, (await GetOrderAsync("WS-1")).Attempts);
    }

    [Fact]
    public async Task Next_automatic_retry_time_follows_the_backoff()
    {
        await AddOrderAsync("WS-1");
        moneybird.CreateInvoiceError = new MoneybirdException("tijdelijke fout");
        await RunAsync();

        var order = await GetOrderAsync("WS-1");
        Assert.Equal(Now + OrderInvoicingService.RetryDelayAfter(1), OrderInvoicingService.NextAutomaticRetryAt(order));
        Assert.True(OrderInvoicingService.RetryDelayAfter(2) > OrderInvoicingService.RetryDelayAfter(1));
    }

    [Fact]
    public async Task Manual_retry_skips_the_backoff()
    {
        await AddOrderAsync("WS-1");
        moneybird.CreateInvoiceError = new MoneybirdException("tijdelijke fout");
        await RunAsync();

        moneybird.CreateInvoiceError = null;
        var order = await GetOrderAsync("WS-1");
        Assert.True(await CreateService().RetryOrderAsync(order.Id));
        Assert.Equal(SyncStatus.Synced, (await GetOrderAsync("WS-1")).Status);
    }

    [Fact]
    public async Task Automatic_retries_stop_after_max_attempts_but_manual_retry_works()
    {
        await AddOrderAsync("WS-1");
        moneybird.CreateInvoiceError = new MoneybirdException("blijvende fout");
        for (var i = 0; i < OrderInvoicingService.MaxAutomaticAttempts + 2; i++)
        {
            await RunAsync();
            time.Advance(TimeSpan.FromHours(1));
        }

        var order = await GetOrderAsync("WS-1");
        Assert.Equal(OrderInvoicingService.MaxAutomaticAttempts, order.Attempts);

        moneybird.CreateInvoiceError = null;
        Assert.True(await CreateService().RetryOrderAsync(order.Id));
        Assert.Equal(SyncStatus.Synced, (await GetOrderAsync("WS-1")).Status);
    }

    [Fact]
    public async Task Rate_limit_stops_the_run()
    {
        await AddOrderAsync("WS-1");
        await AddOrderAsync("WS-2");
        moneybird.CreateInvoiceError = new MoneybirdException("rate limit", HttpStatusCode.TooManyRequests);

        var result = await RunAsync();

        Assert.True(result.RateLimited);
        Assert.Equal(SyncStatus.Failed, (await GetOrderAsync("WS-1")).Status);
        Assert.Equal(SyncStatus.Pending, (await GetOrderAsync("WS-2")).Status);
    }

    [Fact]
    public async Task Order_missing_in_shop_fails_with_reason()
    {
        await AddOrderAsync("WS-1");
        shop.Orders.Clear();

        await RunAsync();

        var order = await GetOrderAsync("WS-1");
        Assert.Equal(SyncStatus.Failed, order.Status);
        Assert.Contains("niet gevonden in de webshop", order.LastError);
        Assert.Empty(moneybird.CreatedInvoices);
    }

    [Fact]
    public async Task Recent_processing_order_is_left_alone()
    {
        await AddOrderAsync("WS-1", SyncStatus.Processing, lastAttemptAt: Now.AddMinutes(-1));

        await RunAsync();

        Assert.Equal(0, moneybird.FindInvoiceCalls);
        Assert.Equal(SyncStatus.Processing, (await GetOrderAsync("WS-1")).Status);
    }

    [Fact]
    public async Task Stale_processing_order_is_resumed_without_duplicate()
    {
        // Simulates a crash after Moneybird created the invoice but before we saved it.
        await AddOrderAsync("WS-1", SyncStatus.Processing, lastAttemptAt: Now.AddMinutes(-11));
        moneybird.Invoices.Add(new MoneybirdInvoice("inv-before-crash", "WS-1"));

        await RunAsync();

        Assert.Empty(moneybird.CreatedInvoices);
        var order = await GetOrderAsync("WS-1");
        Assert.Equal(SyncStatus.Synced, order.Status);
        Assert.Equal("inv-before-crash", order.MoneybirdInvoiceId);
    }

    [Fact]
    public async Task Nothing_happens_when_moneybird_is_not_configured()
    {
        await AddOrderAsync("WS-1");
        moneybird.IsConfigured = false;

        var result = await RunAsync();

        Assert.True(result.NotConfigured);
        Assert.Equal(0, moneybird.FindInvoiceCalls);
        var order = await GetOrderAsync("WS-1");
        Assert.Equal(SyncStatus.Pending, order.Status);
        Assert.Equal(0, order.Attempts);
    }

    private async Task<InvoiceRunResult> RunAsync() => await CreateService().InvoicePendingOrdersAsync();

    private OrderInvoicingService CreateService(OrderChangeNotifier? notifier = null, SandboxSimulationService? simulation = null)
    {
        var db = new InvoiceBridgeDbContext(dbOptions);
        var shopClient = new MockShopClient(new HttpClient(shop, disposeHandler: false) { BaseAddress = new Uri("http://shop.test/") });
        // Use a high limit so existing tests never trigger simulation mode.
        var opts = Microsoft.Extensions.Options.Options.Create(new MoneybirdOptions { SandboxInvoiceLimit = 1000 });
        return new OrderInvoicingService(db, moneybird, shopClient, notifier ?? new OrderChangeNotifier(), time, opts,
            simulation ?? new SandboxSimulationService(NullLogger<SandboxSimulationService>.Instance),
            NullLogger<OrderInvoicingService>.Instance);
    }

    private async Task AddOrderAsync(string orderId, SyncStatus status = SyncStatus.Pending,
        DateTimeOffset? lastAttemptAt = null, string customer = "Sanne de Vries", MockOrderLine[]? lines = null)
    {
        shop.Orders.Add(new MockOrder(orderId, customer, $"klant-{orderId}@example.nl",
            new DateTimeOffset(2026, 10, 1, 9, 30, 0, TimeSpan.FromHours(2)),
            lines ?? [new MockOrderLine("Koffiebonen", 2, 12.50m)]));

        using var db = new InvoiceBridgeDbContext(dbOptions);
        db.SyncedOrders.Add(new SyncedOrder
        {
            OrderId = orderId,
            Status = status,
            LastAttemptAt = lastAttemptAt,
            Attempts = status == SyncStatus.Processing ? 1 : 0
        });
        await db.SaveChangesAsync();
    }

    private async Task<SyncedOrder> GetOrderAsync(string orderId)
    {
        using var db = new InvoiceBridgeDbContext(dbOptions);
        return await db.SyncedOrders.AsNoTracking().Include(o => o.InvoiceLines).SingleAsync(o => o.OrderId == orderId);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }

    private sealed class ShopHandler : HttpMessageHandler
    {
        public List<MockOrder> Orders { get; } = [];
        public bool Unavailable { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Unavailable)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            var id = Uri.UnescapeDataString(request.RequestUri!.Segments[^1]);
            var order = Orders.FirstOrDefault(o => o.OrderId == id);
            return Task.FromResult(order is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(order) });
        }
    }

    /// <summary>In-memory Moneybird that records what would have been created.</summary>
    private sealed class FakeMoneybird : IMoneybirdClient
    {
        public bool IsConfigured { get; set; } = true;
        public List<MoneybirdInvoice> Invoices { get; } = [];
        public List<MoneybirdContact> Contacts { get; } = [];
        public List<NewMoneybirdInvoice> CreatedInvoices { get; } = [];
        public List<NewMoneybirdContact> CreatedContacts { get; } = [];
        public int FindInvoiceCalls { get; private set; }
        public Exception? FindInvoiceError { get; set; }
        public Exception? CreateInvoiceError { get; set; }
        public Func<Task>? BeforeFindInvoice { get; set; }

        public async Task<MoneybirdInvoice?> FindInvoiceByReferenceAsync(string reference, CancellationToken cancellationToken = default)
        {
            FindInvoiceCalls++;
            if (BeforeFindInvoice is { } hook) await hook();
            if (FindInvoiceError is not null) throw FindInvoiceError;
            return Invoices.FirstOrDefault(i => i.Reference == reference);
        }

        public Task<MoneybirdContact?> FindContactByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(Contacts.FirstOrDefault(c => c.CustomerId == MoneybirdClient.CustomerIdFor(email)));

        public Task<MoneybirdContact> CreateContactAsync(NewMoneybirdContact contact, CancellationToken cancellationToken = default)
        {
            CreatedContacts.Add(contact);
            var created = new MoneybirdContact($"c-{CreatedContacts.Count}", MoneybirdClient.CustomerIdFor(contact.Email));
            Contacts.Add(created);
            return Task.FromResult(created);
        }

        public Task<MoneybirdInvoice> CreateInvoiceAsync(NewMoneybirdInvoice invoice, CancellationToken cancellationToken = default)
        {
            if (CreateInvoiceError is not null) throw CreateInvoiceError;
            CreatedInvoices.Add(invoice);
            var created = new MoneybirdInvoice($"inv-{CreatedInvoices.Count}", invoice.Reference);
            Invoices.Add(created);
            return Task.FromResult(created);
        }

        public Task<MoneybirdInvoiceDetails> GetInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
