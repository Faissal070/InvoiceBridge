namespace InvoiceBridge.Models;

/// <summary>
/// Outcome of a manual retry from the demo: synced or not, or a friendly reason it was not attempted.
/// </summary>
public record DemoRetryResult(bool Synced, string? RefusedReason)
{
    public bool Attempted => RefusedReason is null;
}
