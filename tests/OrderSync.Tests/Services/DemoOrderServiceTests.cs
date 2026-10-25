using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OrderSync.Clients;
using OrderSync.Data;
using OrderSync.MockShop;
using OrderSync.Models;
using OrderSync.Options;
using OrderSync.Services;

namespace OrderSync.Tests.Services;

public sealed class DemoOrderServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly DbContextOptions<OrderSyncDbContext> dbOptions;
    private readonly MockOrderStore store = new();
    private readonly FakeMoneybird moneybird = new();
    private readonly DemoOptions settings = new() { MaxOrdersPerVisitor = 100, MaxOrdersPerDay = 100, MaxOrdersTotal = 100 };
    private readonly Lazy<DemoRateLimiter> limiter;

    public DemoOrderServiceTests()
    {
        connection.Open();
        dbOptions = new DbContextOptionsBuilder<OrderSyncDbContext>().UseSqlite(connection).Options;
        using var db = new OrderSyncDbContext(dbOptions);
        db.Database.EnsureCreated();
        // Created on first use, so tests can change the settings first.
        limiter = new(() => new DemoRateLimiter(Microsoft.Extensions.Options.Options.Create(settings)));
    }

    public void Dispose()
    {
        if (limiter.IsValueCreated) limiter.Value.Dispose();
        connection.Dispose();
    }

    [Fact]
    public async Task Placed_order_is_in_the_webshop_and_registered_as_demo()
    {
        var result = await CreateService().PlaceOrderAsync("visitor-1", simulateOutage: false);

        Assert.True(result.Accepted);
        Assert.Equal("WS-20261021", result.Order!.OrderId);
        Assert.Same(result.Order, store.Find("WS-20261021"));

        using var db = new OrderSyncDbContext(dbOptions);
        var registered = await db.SyncedOrders.SingleAsync();
        Assert.Equal(result.SyncedOrderId, registered.Id);
        Assert.True(registered.IsDemo);
        Assert.Equal(SyncStatus.Pending, registered.Status);
        Assert.Equal(Now, registered.CreatedAt);
    }

    [Fact]
    public async Task Order_ids_continue_after_the_highest_known_order()
    {
        // E.g. after a restart: the webshop forgot its demo orders, the database did not.
        await AddDemoOrderAsync("WS-20261050", Now.AddDays(-3));

        var result = await CreateService().PlaceOrderAsync("visitor-1", simulateOutage: false);

        Assert.Equal("WS-20261051", result.Order!.OrderId);
    }

    [Fact]
    public async Task Visitor_limit_refuses_with_friendly_message_but_others_can_continue()
    {
        settings.MaxOrdersPerVisitor = 2;
        var service = CreateService();

        await service.PlaceOrderAsync("visitor-1", false);
        await service.PlaceOrderAsync("visitor-1", false);
        var third = await service.PlaceOrderAsync("visitor-1", false);
        var other = await service.PlaceOrderAsync("visitor-2", false);

        Assert.False(third.Accepted);
        Assert.StartsWith("Je hebt net al een paar bestellingen geplaatst.", third.RefusedReason);
        Assert.True(other.Accepted);
    }

    [Fact]
    public async Task Daily_limit_counts_only_todays_demo_orders()
    {
        settings.MaxOrdersPerDay = 2;
        await AddDemoOrderAsync("WS-20261030", Now.AddDays(-1));
        await AddDemoOrderAsync("WS-20261031", Now.AddHours(-1));
        await AddDemoOrderAsync("WS-20261032", Now.AddHours(-1), isDemo: false);

        var allowed = await CreateService().PlaceOrderAsync("visitor-1", false);
        var refused = await CreateService().PlaceOrderAsync("visitor-2", false);

        Assert.True(allowed.Accepted);
        Assert.False(refused.Accepted);
        Assert.Contains("voor vandaag", refused.RefusedReason);
    }

    [Fact]
    public async Task Total_limit_protects_the_sandbox()
    {
        settings.MaxOrdersTotal = 1;
        await AddDemoOrderAsync("WS-20261030", Now.AddDays(-30));

        var result = await CreateService().PlaceOrderAsync("visitor-1", false);

        Assert.False(result.Accepted);
        Assert.Contains("maximale aantal testfacturen", result.RefusedReason);
        Assert.Equal(20, store.GetAll().Count);
    }

    [Fact]
    public async Task Concurrent_visitors_cannot_exceed_the_daily_limit()
    {
        settings.MaxOrdersPerDay = 5;

        var results = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(i => Task.Run(() => CreateService().PlaceOrderAsync($"visitor-{i}", false))));

        var placed = results.Where(r => r.Accepted).ToList();
        Assert.Equal(5, placed.Count);
        Assert.Equal(5, placed.Select(r => r.Order!.OrderId).Distinct().Count());
    }

    [Fact]
    public async Task Nothing_is_placed_when_moneybird_is_not_configured()
    {
        moneybird.IsConfigured = false;

        var result = await CreateService().PlaceOrderAsync("visitor-1", false);

        Assert.False(result.Accepted);
        Assert.Equal(20, store.GetAll().Count);
    }

    [Fact]
    public async Task Order_with_outage_fails_first_and_retry_succeeds_with_cooldown()
    {
        var placed = await CreateService().PlaceOrderAsync("visitor-1", simulateOutage: true);
        var id = placed.SyncedOrderId!.Value;

        Assert.False(await CreateInvoicing().InvoiceOrderAsync(id));
        var retry = await CreateService().RetryAsync("visitor-1", id);
        var tooSoon = await CreateService().RetryAsync("visitor-1", id);

        Assert.True(retry.Synced);
        Assert.False(tooSoon.Attempted);
        Assert.NotNull(tooSoon.RefusedReason);
        Assert.Equal(1, moneybird.CreatedInvoices);
    }

    private DemoOrderService CreateService() => new(
        new OrderSyncDbContext(dbOptions), store, limiter.Value, CreateInvoicing(), new OrderChangeNotifier(),
        Microsoft.Extensions.Options.Options.Create(settings), new FixedTime(Now), NullLogger<DemoOrderService>.Instance);

    private OrderInvoicingService CreateInvoicing()
    {
        var shop = new MockShopClient(new HttpClient(new StoreHandler(store)) { BaseAddress = new Uri("http://shop.test/") });
        return new OrderInvoicingService(new OrderSyncDbContext(dbOptions), moneybird, shop, new OrderChangeNotifier(),
            new FixedTime(Now), NullLogger<OrderInvoicingService>.Instance);
    }

    private async Task AddDemoOrderAsync(string orderId, DateTimeOffset createdAt, bool isDemo = true)
    {
        using var db = new OrderSyncDbContext(dbOptions);
        db.SyncedOrders.Add(new SyncedOrder { OrderId = orderId, IsDemo = isDemo, CreatedAt = createdAt });
        await db.SaveChangesAsync();
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Serves single orders from the store, including its simulated outages, like the real endpoint.</summary>
    private sealed class StoreHandler(MockOrderStore store) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var id = Uri.UnescapeDataString(request.RequestUri!.Segments[^1]);
            if (store.TryConsumeOutage(id))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }
            return Task.FromResult(store.Find(id) is { } order
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(order) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FakeMoneybird : IMoneybirdClient
    {
        private int createdInvoices;

        public bool IsConfigured { get; set; } = true;

        public int CreatedInvoices => createdInvoices;

        public Task<MoneybirdInvoice?> FindInvoiceByReferenceAsync(string reference, CancellationToken cancellationToken = default) =>
            Task.FromResult<MoneybirdInvoice?>(null);

        public Task<MoneybirdContact?> FindContactByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult<MoneybirdContact?>(new MoneybirdContact("c-1", MoneybirdClient.CustomerIdFor(email)));

        public Task<MoneybirdContact> CreateContactAsync(NewMoneybirdContact contact, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MoneybirdInvoice> CreateInvoiceAsync(NewMoneybirdInvoice invoice, CancellationToken cancellationToken = default)
        {
            var n = Interlocked.Increment(ref createdInvoices);
            return Task.FromResult(new MoneybirdInvoice($"inv-{n}", invoice.Reference));
        }
    }
}
