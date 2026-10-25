namespace OrderSync.Models;

public record NewMoneybirdInvoice(
    string ContactId,
    string Reference,
    DateOnly InvoiceDate,
    IReadOnlyList<NewMoneybirdInvoiceLine> Lines);
