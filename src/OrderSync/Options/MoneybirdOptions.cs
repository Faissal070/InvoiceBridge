namespace OrderSync.Options;

/// <summary>
/// Moneybird settings. Token and AdministrationId come from user-secrets, never from appsettings.
/// </summary>
public class MoneybirdOptions
{
    public const string SectionName = "Moneybird";

    public string BaseUrl { get; set; } = "https://moneybird.com/api/v2/";

    public string? Token { get; set; }

    public string? AdministrationId { get; set; }

    /// <summary>Optional tax rate for invoice lines; Moneybird's default is used when empty.</summary>
    public string? TaxRateId { get; set; }

    /// <summary>Maximum invoices per calendar month before switching to simulation mode. Default is 45 (5-invoice safety margin below Moneybird's sandbox limit of 50).</summary>
    public int SandboxInvoiceLimit { get; init; } = 45;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Token) && !string.IsNullOrWhiteSpace(AdministrationId);
}
