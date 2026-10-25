using OrderSync.Models;

namespace OrderSync.Clients;

/// <summary>
/// Typed HTTP client for the (mock) webshop orders API.
/// </summary>
public class MockShopClient(HttpClient http)
{
    public async Task<IReadOnlyList<MockOrder>> GetOrdersAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<List<MockOrder>>("mockshop/orders", cancellationToken) ?? [];

    public async Task<MockOrder?> GetOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"mockshop/orders/{Uri.EscapeDataString(orderId)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MockOrder>(cancellationToken);
    }
}
