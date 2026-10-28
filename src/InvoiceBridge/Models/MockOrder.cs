namespace InvoiceBridge.Models;

public record MockOrder(
    string OrderId,
    string CustomerName,
    string Email,
    DateTimeOffset OrderDate,
    IReadOnlyList<MockOrderLine> Lines)
{
    public decimal Total => Lines.Sum(l => l.LineTotal);
}
