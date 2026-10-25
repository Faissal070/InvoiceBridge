using OrderSync.Models;

namespace OrderSync.MockShop;

/// <summary>
/// Fake demo orders for the mock webshop. All names and e-mail addresses are made up.
/// </summary>
public static class MockOrderSeed
{
    private static readonly DateTimeOffset BaseDate = new(2026, 10, 1, 9, 0, 0, TimeSpan.FromHours(2));

    public static IReadOnlyList<MockOrder> Orders { get; } =
    [
        Order(1, "Sanne de Vries", "sanne.devries@example.nl", 0, 9.5,
            Line("Koffiebonen Ethiopië 1 kg", 1, 24.95m),
            Line("Filterzakjes nr. 4 (100 st.)", 2, 3.49m)),
        Order(2, "Daan Jansen", "daan.jansen@example.nl", 0, 14.25,
            Line("Theedoek linnen grijs", 3, 8.95m)),
        Order(3, "Emma Bakker", "emma.bakker@example.nl", 1, 11,
            Line("Stroopwafels ambachtelijk (12 st.)", 2, 6.50m),
            Line("Theemok aardewerk", 2, 12.95m)),
        Order(4, "Lucas Visser", "lucas.visser@example.nl", 2, 16.75,
            Line("Espressomachine handmatig", 1, 149.00m)),
        Order(5, "Julia Smit", "julia.smit@example.nl", 3, 8.5,
            Line("Losse thee Earl Grey 250 g", 1, 7.95m),
            Line("Theefilter RVS", 1, 9.95m),
            Line("Honing bloemen 450 g", 1, 6.25m)),
        Order(6, "Sem Meijer", "sem.meijer@example.nl", 4, 20,
            Line("Koffiebonen Colombia 500 g", 4, 13.95m)),
        Order(7, "Tess de Boer", "tess.deboer@example.nl", 5, 10.5,
            Line("Snijplank eikenhout", 1, 39.95m),
            Line("Theedoek linnen blauw", 2, 8.95m)),
        Order(8, "Finn Mulder", "finn.mulder@example.nl", 6, 13,
            Line("Melkopschuimer", 1, 29.95m)),
        Order(9, "Lotte de Groot", "lotte.degroot@example.nl", 7, 9,
            Line("Kruidnoten chocolade 500 g", 3, 5.49m),
            Line("Chocolademelk poeder", 1, 4.99m)),
        Order(10, "Milan Bos", "milan.bos@example.nl", 8, 15.5,
            Line("Pour-over set glas", 1, 34.50m),
            Line("Koffiebonen Brazilië 1 kg", 1, 22.95m)),
        Order(11, "Sophie Vos", "sophie.vos@example.nl", 9, 12,
            Line("Theepot gietijzer", 1, 44.95m)),
        Order(12, "Noah Peters", "noah.peters@example.nl", 10, 18.25,
            Line("Filterzakjes nr. 4 (100 st.)", 5, 3.49m),
            Line("Koffiemolen elektrisch", 1, 59.00m)),
        Order(13, "Anna Hendriks", "anna.hendriks@example.nl", 11, 10.75,
            Line("Theedoek linnen grijs", 2, 8.95m),
            Line("Ovenwant katoen", 2, 11.50m)),
        Order(14, "Levi van Leeuwen", "levi.vanleeuwen@example.nl", 12, 21.5,
            Line("Koffiebonen Ethiopië 1 kg", 2, 24.95m)),
        Order(15, "Isa Dekker", "isa.dekker@example.nl", 13, 9.25,
            Line("Losse thee groene sencha 100 g", 2, 8.50m),
            Line("Theemok aardewerk", 1, 12.95m)),
        Order(16, "Thijs Brouwer", "thijs.brouwer@example.nl", 15, 14,
            Line("Stroopwafels ambachtelijk (12 st.)", 6, 6.50m)),
        Order(17, "Fleur de Wit", "fleur.dewit@example.nl", 17, 11.5,
            Line("Espressokopjes set van 4", 1, 24.95m),
            Line("Koffiebonen Colombia 500 g", 1, 13.95m)),
        Order(18, "Jesse Dijkstra", "jesse.dijkstra@example.nl", 19, 16,
            Line("Snijplank eikenhout", 1, 39.95m)),
        Order(19, "Eva Smits", "eva.smits@example.nl", 21, 8.75,
            Line("Honing bloemen 450 g", 2, 6.25m),
            Line("Ontbijtkoek Fries 500 g", 2, 3.95m)),
        Order(20, "Ruben van Dam", "ruben.vandam@example.nl", 23, 19.5,
            Line("Melkopschuimer", 1, 29.95m),
            Line("Koffiebonen Brazilië 1 kg", 2, 22.95m),
            Line("Espressokopjes set van 4", 1, 24.95m)),
    ];

    private static MockOrder Order(int number, string name, string email, int day, double hour, params MockOrderLine[] lines) =>
        new($"WS-{2026_1000 + number}", name, email, BaseDate.AddDays(day).AddHours(hour - 9), lines);

    private static MockOrderLine Line(string product, int quantity, decimal unitPrice) =>
        new(product, quantity, unitPrice);
}
