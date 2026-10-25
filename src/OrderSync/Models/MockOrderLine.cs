namespace OrderSync.Models;

public record MockOrderLine(string Product, int Quantity, decimal UnitPrice)
{
    public decimal LineTotal => Quantity * UnitPrice;
}
