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
}
