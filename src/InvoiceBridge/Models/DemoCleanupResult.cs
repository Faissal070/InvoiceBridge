namespace InvoiceBridge.Models;

/// <summary>
/// Outcome of a demo cleanup: removed order ids, and unfinished ones kept because Moneybird already has an invoice for them.
/// </summary>
public record DemoCleanupResult(IReadOnlyList<string> Removed, IReadOnlyList<string> KeptWithInvoice);
