using OrderSync.Models;

namespace OrderSync.MockShop;

/// <summary>
/// Creates random demo orders. All names and e-mail addresses are made up.
/// </summary>
public static class DemoOrderFactory
{
    private static readonly string[] FirstNames =
        ["Sanne", "Daan", "Emma", "Lucas", "Julia", "Sem", "Tess", "Finn", "Lotte", "Milan",
         "Sophie", "Noah", "Anna", "Levi", "Isa", "Thijs", "Fleur", "Jesse", "Eva", "Ruben"];

    private static readonly string[] LastNames =
        ["de Vries", "Jansen", "Bakker", "Visser", "Smit", "Meijer", "de Boer", "Mulder", "de Groot", "Bos",
         "Vos", "Peters", "Hendriks", "van Leeuwen", "Dekker", "Brouwer", "de Wit", "Dijkstra", "Smits", "van Dam"];

    /// <summary>The seed assortment, so demo orders contain the same products and prices.</summary>
    private static readonly MockOrderLine[] Products = MockOrderSeed.Orders
        .SelectMany(o => o.Lines)
        .DistinctBy(l => l.Product)
        .ToArray();

    public static MockOrder Create(string orderId, DateTimeOffset orderDate, Random random)
    {
        var firstName = FirstNames[random.Next(FirstNames.Length)];
        var lastName = LastNames[random.Next(LastNames.Length)];
        var email = $"{firstName}.{lastName.Replace(" ", "")}@example.nl".ToLowerInvariant();

        var lines = Products
            .OrderBy(_ => random.Next())
            .Take(random.Next(1, 4))
            .Select(p => new MockOrderLine(p.Product, random.Next(1, 4), p.UnitPrice))
            .ToList();

        return new MockOrder(orderId, $"{firstName} {lastName}", email, orderDate, lines);
    }
}
