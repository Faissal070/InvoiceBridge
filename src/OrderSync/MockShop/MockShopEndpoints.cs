using OrderSync.Models;

namespace OrderSync.MockShop;

/// <summary>
/// Minimal API that simulates an external webshop exposing its orders.
/// </summary>
public static class MockShopEndpoints
{
    public static IEndpointRouteBuilder MapMockShop(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/mockshop/orders").WithTags("MockShop");

        orders.MapGet("/", (DateTimeOffset? since) =>
            MockOrderSeed.Orders
                .Where(o => since is null || o.OrderDate > since)
                .OrderBy(o => o.OrderDate));

        orders.MapGet("/{orderId}", (string orderId) =>
            MockOrderSeed.Orders.FirstOrDefault(o => o.OrderId == orderId) is { } order
                ? Results.Ok(order)
                : Results.NotFound());

        return app;
    }
}
