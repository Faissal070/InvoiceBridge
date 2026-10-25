using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderSync.MockShop;
using OrderSync.Models;
using OrderSync.Workers;

namespace OrderSync.Tests.MockShop;

public sealed class MockShopAppFactory : WebApplicationFactory<Program>
{
    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"ordersync-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:OrderSync", $"Data Source={dbPath};Pooling=False");
        builder.ConfigureTestServices(services =>
        {
            // Don't poll in tests; the worker would call a real localhost port.
            var worker = services.Single(d => d.ImplementationType == typeof(OrderSyncWorker));
            services.Remove(worker);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        File.Delete(dbPath);
    }
}

public class MockShopEndpointsTests(MockShopAppFactory factory) : IClassFixture<MockShopAppFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task Get_orders_returns_all_seeded_orders_sorted_by_date()
    {
        var orders = await client.GetFromJsonAsync<List<MockOrder>>("/mockshop/orders");

        Assert.NotNull(orders);
        Assert.Equal(20, orders.Count);
        Assert.Equal(orders.OrderBy(o => o.OrderDate).Select(o => o.OrderId), orders.Select(o => o.OrderId));
    }

    [Fact]
    public async Task Get_orders_with_since_returns_only_newer_orders()
    {
        var since = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.FromHours(2));

        var orders = await client.GetFromJsonAsync<List<MockOrder>>(
            $"/mockshop/orders?since={Uri.EscapeDataString(since.ToString("O"))}");

        Assert.NotNull(orders);
        Assert.NotEmpty(orders);
        Assert.All(orders, o => Assert.True(o.OrderDate > since));
        Assert.Equal(MockOrderSeed.Orders.Count(o => o.OrderDate > since), orders.Count);
    }

    [Fact]
    public async Task Get_order_by_id_returns_order()
    {
        var expected = MockOrderSeed.Orders[4];

        var order = await client.GetFromJsonAsync<MockOrder>($"/mockshop/orders/{expected.OrderId}");

        Assert.NotNull(order);
        Assert.Equal(expected.OrderId, order.OrderId);
        Assert.Equal(expected.CustomerName, order.CustomerName);
        Assert.Equal(expected.Lines.Count, order.Lines.Count);
    }

    [Fact]
    public async Task Get_unknown_order_returns_404()
    {
        var response = await client.GetAsync("/mockshop/orders/bestaat-niet");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
