namespace OrderSync.Options;

/// <summary>
/// Admin access settings. AdminCode must be set via user-secrets (locally) or an environment variable / app setting (Azure).
/// Never store this value in appsettings.json or source control.
/// </summary>
public class AdminOptions
{
    public const string SectionName = "Admin";

    public string? AdminCode { get; init; }
}
