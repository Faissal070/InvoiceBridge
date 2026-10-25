namespace OrderSync.Models;

/// <summary>
/// One line of the invoice as it was sent to Moneybird, so it can be shown without calling Moneybird.
/// </summary>
public class InvoiceLine
{
    public int Id { get; set; }

    public int SyncedOrderId { get; set; }

    public required string Description { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineTotal => Quantity * UnitPrice;
}
