namespace OrderSync.Moneybird;

public record MoneybirdContact(string Id, string? Email, string? SendInvoicesToEmail);

public record MoneybirdInvoice(string Id, string? Reference);

public record NewMoneybirdContact(string FirstName, string LastName, string Email);

public record NewMoneybirdInvoiceLine(string Description, int Quantity, decimal Price);

public record NewMoneybirdInvoice(
    string ContactId,
    string Reference,
    DateOnly InvoiceDate,
    IReadOnlyList<NewMoneybirdInvoiceLine> Lines);
