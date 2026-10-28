using System.Globalization;

namespace InvoiceBridge.Models;

public record MoneybirdInvoiceDetails(string Id, string? InvoiceId, string State, DateTimeOffset CreatedAt)
{
    public string StateLabel => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en"
        ? State switch
        {
            "draft" => "Draft",
            "open" => "Sent",
            "scheduled" => "Scheduled",
            "pending_payment" => "Pending payment",
            "late" => "Overdue",
            "reminded" => "Reminder sent",
            "paid" => "Paid",
            _ => State
        }
        : State switch
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
