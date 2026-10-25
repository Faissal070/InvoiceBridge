namespace OrderSync.MockShop;

public record MockOrderLine(string Product, int Quantity, decimal UnitPrice)
{
    public decimal LineTotal => Quantity * UnitPrice;
}

public record MockOrder(
    string OrderId,
    string CustomerName,
    string Email,
    DateTimeOffset OrderDate,
    IReadOnlyList<MockOrderLine> Lines)
{
    public decimal Total => Lines.Sum(l => l.LineTotal);
}
