namespace InvoiceBridge.Models;

/// <summary>
/// Outcome of placing a demo order: either the placed order, or a friendly reason why not.
/// </summary>
public record DemoOrderResult(int? SyncedOrderId, MockOrder? Order, string? RefusedReason)
{
    public bool Accepted => RefusedReason is null;

    public static DemoOrderResult Placed(int syncedOrderId, MockOrder order) => new(syncedOrderId, order, null);

    public static DemoOrderResult Refused(string reason) => new(null, null, reason);
}
