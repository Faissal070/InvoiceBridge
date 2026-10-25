using OrderSync.Models;

namespace OrderSync.Clients;

public interface IMoneybirdClient
{
    bool IsConfigured { get; }

    Task<MoneybirdInvoice?> FindInvoiceByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<MoneybirdContact?> FindContactByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<MoneybirdContact> CreateContactAsync(NewMoneybirdContact contact, CancellationToken cancellationToken = default);

    Task<MoneybirdInvoice> CreateInvoiceAsync(NewMoneybirdInvoice invoice, CancellationToken cancellationToken = default);
}
