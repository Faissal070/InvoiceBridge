namespace OrderSync.Models;

/// <summary>
/// Tracks the sync state of a single webshop order towards Moneybird.
/// </summary>
public class SyncedOrder
{
    public int Id { get; set; }

    public required string OrderId { get; set; }

    public SyncStatus Status { get; set; } = SyncStatus.Pending;

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public string? MoneybirdInvoiceId { get; set; }

    public DateTimeOffset? SyncedAt { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Placed by a visitor of the live demo; counts towards the demo limits.</summary>
    public bool IsDemo { get; set; }

    // Snapshot of the invoice, filled in when the order is synced.

    public string? CustomerName { get; set; }

    public DateOnly? InvoiceDate { get; set; }

    public decimal? Total { get; set; }

    public List<InvoiceLine> InvoiceLines { get; set; } = [];
}
