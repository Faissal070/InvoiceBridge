using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using InvoiceBridge.Clients;
using InvoiceBridge.Data;
using InvoiceBridge.Models;
using InvoiceBridge.Services;

namespace InvoiceBridge.Tests.Services;

public sealed class DemoCleanupServiceTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly DbContextOptions<InvoiceBridgeDbContext> dbOptions;
    private readonly FakeMoneybird moneybird = new();

    public DemoCleanupServiceTests()
    {
        connection.Open();
        dbOptions = new DbContextOptionsBuilder<InvoiceBridgeDbContext>().UseSqlite(connection).Options;
        using var db = new InvoiceBridgeDbContext(dbOptions);
        db.Database.EnsureCreated();
    }

    public void Dispose() => connection.Dispose();

    [Fact]
    public async Task Removes_unfinished_demo_orders_without_invoice()
    {
        await AddOrderAsync("WS-1", SyncStatus.Failed);
        await AddOrderAsync("WS-2", SyncStatus.Pending);
        await AddOrderAsync("WS-3", SyncStatus.Processing);

        var result = await CreateService().RemoveUnfinishedDemoOrdersAsync();

        Assert.Equal(["WS-1", "WS-2", "WS-3"], result.Removed.Order());
        Assert.Empty(await OrderIdsAsync());
    }

    [Fact]
    public async Task Keeps_finished_and_non_demo_orders()
    {
        await AddOrderAsync("WS-1", SyncStatus.Synced);
        await AddOrderAsync("WS-2", SyncStatus.Simulated);
        await AddOrderAsync("WS-3", SyncStatus.Failed, isDemo: false);

        var result = await CreateService().RemoveUnfinishedDemoOrdersAsync();

        Assert.Empty(result.Removed);
        Assert.Equal(["WS-1", "WS-2", "WS-3"], await OrderIdsAsync());
    }

    [Fact]
    public async Task Keeps_unfinished_order_that_already_has_a_Moneybird_invoice()
    {
        // Removing it would free the id, and the next demo order would adopt this invoice.
        moneybird.InvoiceReferences.Add("WS-1");
        await AddOrderAsync("WS-1", SyncStatus.Failed);

        var result = await CreateService().RemoveUnfinishedDemoOrdersAsync();

        Assert.Equal(["WS-1"], result.KeptWithInvoice);
        Assert.Empty(result.Removed);
        Assert.Equal(["WS-1"], await OrderIdsAsync());
    }

    [Fact]
    public async Task Refuses_to_run_when_Moneybird_is_not_configured()
    {
        moneybird.IsConfigured = false;
        await AddOrderAsync("WS-1", SyncStatus.Failed);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().RemoveUnfinishedDemoOrdersAsync());
        Assert.Equal(["WS-1"], await OrderIdsAsync());
    }

    private DemoCleanupService CreateService() =>
        new(new InvoiceBridgeDbContext(dbOptions), moneybird, NullLogger<DemoCleanupService>.Instance);

    private async Task AddOrderAsync(string orderId, SyncStatus status, bool isDemo = true)
    {
        using var db = new InvoiceBridgeDbContext(dbOptions);
        db.SyncedOrders.Add(new SyncedOrder { OrderId = orderId, Status = status, IsDemo = isDemo });
        await db.SaveChangesAsync();
    }

    private async Task<List<string>> OrderIdsAsync()
    {
        using var db = new InvoiceBridgeDbContext(dbOptions);
        return await db.SyncedOrders.Select(o => o.OrderId).OrderBy(id => id).ToListAsync();
    }

    private sealed class FakeMoneybird : IMoneybirdClient
    {
        public bool IsConfigured { get; set; } = true;

        public List<string> InvoiceReferences { get; } = [];

        public Task<MoneybirdInvoice?> FindInvoiceByReferenceAsync(string reference, CancellationToken cancellationToken = default) =>
            Task.FromResult(InvoiceReferences.Contains(reference) ? new MoneybirdInvoice($"inv-{reference}", reference) : null);

        public Task<MoneybirdContact?> FindContactByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MoneybirdContact> CreateContactAsync(NewMoneybirdContact contact, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MoneybirdInvoice> CreateInvoiceAsync(NewMoneybirdInvoice invoice, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MoneybirdInvoiceDetails> GetInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
