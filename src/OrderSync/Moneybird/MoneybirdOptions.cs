namespace OrderSync.Moneybird;

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

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Token) && !string.IsNullOrWhiteSpace(AdministrationId);
}
