namespace OrderSync.Models;

public record InvoiceRunResult(int Synced, int Failed, bool RateLimited, bool NotConfigured = false);
