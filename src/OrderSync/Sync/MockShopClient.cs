using OrderSync.MockShop;

namespace OrderSync.Sync;

/// <summary>
/// Typed HTTP client for the (mock) webshop orders API.
/// </summary>
public class MockShopClient(HttpClient http)
{
    public async Task<IReadOnlyList<MockOrder>> GetOrdersAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<List<MockOrder>>("mockshop/orders", cancellationToken) ?? [];
}
