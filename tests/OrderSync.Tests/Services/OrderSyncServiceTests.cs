using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OrderSync.Clients;
using OrderSync.Data;
using OrderSync.Models;
using OrderSync.Services;

namespace OrderSync.Tests.Services;

public sealed class OrderSyncServiceTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly DbContextOptions<OrderSyncDbContext> dbOptions;
    private readonly StubHandler shop = new();

    public OrderSyncServiceTests()
    {
        connection.Open();
        dbOptions = new DbContextOptionsBuilder<OrderSyncDbContext>().UseSqlite(connection).Options;
        using var db = new OrderSyncDbContext(dbOptions);
        db.Database.EnsureCreated();
    }

    public void Dispose() => connection.Dispose();

    [Fact]
    public async Task New_orders_are_stored_as_pending()
    {
        shop.Orders = [Order("WS-1"), Order("WS-2")];

        var count = await RunSyncAsync();

        Assert.Equal(2, count);
        using var db = new OrderSyncDbContext(dbOptions);
        var stored = await db.SyncedOrders.OrderBy(o => o.OrderId).ToListAsync();
        Assert.Equal(["WS-1", "WS-2"], stored.Select(o => o.OrderId));
        Assert.All(stored, o =>
        {
            Assert.Equal(SyncStatus.Pending, o.Status);
            Assert.Equal(0, o.Attempts);
            Assert.Null(o.MoneybirdInvoiceId);
            Assert.Null(o.SyncedAt);
        });
    }

    [Fact]
    public async Task Known_orders_are_not_stored_twice()
    {
        shop.Orders = [Order("WS-1"), Order("WS-2")];
        await RunSyncAsync();

        shop.Orders = [Order("WS-1"), Order("WS-2"), Order("WS-3")];
        var count = await RunSyncAsync();

        Assert.Equal(1, count);
        using var db = new OrderSyncDbContext(dbOptions);
        Assert.Equal(3, await db.SyncedOrders.CountAsync());
    }

    [Fact]
    public async Task Existing_order_state_is_left_untouched()
    {
        using (var db = new OrderSyncDbContext(dbOptions))
        {
            db.SyncedOrders.Add(new SyncedOrder
            {
                OrderId = "WS-1",
                Status = SyncStatus.Synced,
                Attempts = 1,
                MoneybirdInvoiceId = "INV-1"
            });
            await db.SaveChangesAsync();
        }
        shop.Orders = [Order("WS-1")];

        var count = await RunSyncAsync();

        Assert.Equal(0, count);
        using var verify = new OrderSyncDbContext(dbOptions);
        var order = await verify.SyncedOrders.SingleAsync();
        Assert.Equal(SyncStatus.Synced, order.Status);
        Assert.Equal("INV-1", order.MoneybirdInvoiceId);
    }

    [Fact]
    public async Task Empty_shop_stores_nothing()
    {
        var count = await RunSyncAsync();

        Assert.Equal(0, count);
        using var db = new OrderSyncDbContext(dbOptions);
        Assert.Empty(db.SyncedOrders);
    }

    [Fact]
    public async Task Shop_error_is_propagated_and_nothing_is_stored()
    {
        shop.StatusCode = HttpStatusCode.InternalServerError;

        await Assert.ThrowsAsync<HttpRequestException>(RunSyncAsync);

        using var db = new OrderSyncDbContext(dbOptions);
        Assert.Empty(db.SyncedOrders);
    }

    private async Task<int> RunSyncAsync()
    {
        using var db = new OrderSyncDbContext(dbOptions);
        var client = new MockShopClient(new HttpClient(shop, disposeHandler: false)
        {
            BaseAddress = new Uri("http://shop.test/")
        });
        var service = new OrderSyncService(client, db, NullLogger<OrderSyncService>.Instance);
        return await service.SyncNewOrdersAsync();
    }

    private static MockOrder Order(string id) =>
        new(id, "Test Klant", "test@example.nl", DateTimeOffset.UtcNow, [new MockOrderLine("Product", 1, 1m)]);

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<MockOrder> Orders { get; set; } = [];
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/mockshop/orders", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(StatusCode) { Content = JsonContent.Create(Orders) });
        }
    }
}
