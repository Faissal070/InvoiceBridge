namespace OrderSync.MockShop;

/// <summary>
/// Minimal API that simulates an external webshop exposing its orders.
/// </summary>
public static class MockShopEndpoints
{
    public static IEndpointRouteBuilder MapMockShop(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/mockshop/orders").WithTags("MockShop");

        orders.MapGet("/", (DateTimeOffset? since, MockOrderStore store) =>
            store.GetAll().Where(o => since is null || o.OrderDate > since));

        orders.MapGet("/{orderId}", (string orderId, MockOrderStore store) =>
        {
            if (store.TryConsumeOutage(orderId))
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            return store.Find(orderId) is { } order ? Results.Ok(order) : Results.NotFound();
        });

        return app;
    }
}
