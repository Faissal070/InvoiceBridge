namespace OrderSync.Models;

public record MoneybirdInvoiceDetails(string Id, string? InvoiceId, string State, DateTimeOffset CreatedAt)
{
    public string StateLabel => State switch
    {
        "draft" => "Concept",
        "open" => "Verzonden",
        "scheduled" => "Gepland",
        "pending_payment" => "In afwachting van betaling",
        "late" => "Te laat",
        "reminded" => "Herinnering verstuurd",
        "paid" => "Betaald",
        _ => State
    };
}
