namespace OrderSync.Options;

/// <summary>
/// Settings for the public demo. The limits keep the Moneybird sandbox from filling up.
/// </summary>
public class DemoOptions
{
    public const string SectionName = "Demo";

    /// <summary>Demo orders one visitor (IP address) may place per <see cref="VisitorWindowMinutes"/>.</summary>
    public int MaxOrdersPerVisitor { get; set; } = 3;

    public int VisitorWindowMinutes { get; set; } = 10;

    /// <summary>Demo orders for all visitors together per (UTC) day.</summary>
    public int MaxOrdersPerDay { get; set; } = 50;

    /// <summary>Hard cap on demo invoices ever created in the sandbox.</summary>
    public int MaxOrdersTotal { get; set; } = 500;

    /// <summary>Minimum time between two manual retries by the same visitor.</summary>
    public int RetryCooldownSeconds { get; set; } = 10;

    /// <summary>Show links to Moneybird. Only turn on for your own instance; visitors have no access.</summary>
    public bool ShowMoneybirdLinks { get; set; }

    /// <summary>Estimate of the manual work one invoice takes, for the "time saved" figure.</summary>
    public int MinutesSavedPerInvoice { get; set; } = 5;
}
