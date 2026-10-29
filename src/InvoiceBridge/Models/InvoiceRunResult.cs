namespace InvoiceBridge.Models;

public record InvoiceRunResult(int Synced, int Failed, bool RateLimited, int Simulated = 0);
