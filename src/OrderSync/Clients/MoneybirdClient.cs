using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using OrderSync.Exceptions;
using OrderSync.Models;
using OrderSync.Options;

namespace OrderSync.Clients;

/// <summary>
/// Typed HTTP client for the Moneybird v2 API. Invoices are created as drafts and never sent.
/// </summary>
public class MoneybirdClient(HttpClient http, IOptions<MoneybirdOptions> options) : IMoneybirdClient
{
    private const int MaxErrorBodyLength = 300;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly MoneybirdOptions settings = options.Value;

    public bool IsConfigured => settings.IsConfigured;

    public async Task<MoneybirdInvoice?> FindInvoiceByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        // Explicit state:all without period, so invoices from any date are included.
        var filter = Uri.EscapeDataString($"reference:{reference},state:all");
        var invoices = await SendAsync<List<InvoiceDto>>(HttpMethod.Get, $"sales_invoices.json?filter={filter}", null, cancellationToken);

        // The filter is a match on reference; double-check to be safe.
        return invoices?
            .Where(i => i.Reference == reference)
            .Select(i => new MoneybirdInvoice(i.Id, i.Reference))
            .FirstOrDefault();
    }

    public async Task<MoneybirdContact?> FindContactByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        // The free-text contact search is not reliable right after creation, so contacts are
        // looked up by a customer_id derived from the e-mail address instead.
        var path = $"contacts/customer_id/{CustomerIdFor(email)}.json";
        var contact = await SendAsync<ContactDto>(HttpMethod.Get, path, null, cancellationToken, notFoundIsNull: true);
        return contact is null ? null : ToContact(contact);
    }

    public async Task<MoneybirdContact> CreateContactAsync(NewMoneybirdContact contact, CancellationToken cancellationToken = default)
    {
        // The e-mail address itself is not sent: Moneybird rejects the fake demo domains,
        // and invoices are never e-mailed anyway (delivery_method Manual).
        var body = new
        {
            contact = new
            {
                firstname = contact.FirstName,
                lastname = contact.LastName,
                customer_id = CustomerIdFor(contact.Email),
                delivery_method = "Manual"
            }
        };
        var created = await SendAsync<ContactDto>(HttpMethod.Post, "contacts.json", body, cancellationToken)
            ?? throw new MoneybirdException("Moneybird gaf een leeg antwoord bij het aanmaken van het contact.");
        return ToContact(created);
    }

    public async Task<MoneybirdInvoice> CreateInvoiceAsync(NewMoneybirdInvoice invoice, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            sales_invoice = new
            {
                contact_id = invoice.ContactId,
                reference = invoice.Reference,
                invoice_date = invoice.InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                prices_are_incl_tax = true,
                details_attributes = invoice.Lines.Select(l => new
                {
                    description = l.Description,
                    amount = l.Quantity.ToString(CultureInfo.InvariantCulture),
                    price = l.Price.ToString(CultureInfo.InvariantCulture),
                    tax_rate_id = string.IsNullOrWhiteSpace(settings.TaxRateId) ? null : settings.TaxRateId
                })
            }
        };
        var created = await SendAsync<InvoiceDto>(HttpMethod.Post, "sales_invoices.json", body, cancellationToken)
            ?? throw new MoneybirdException("Moneybird gaf een leeg antwoord bij het aanmaken van de factuur.");
        return new MoneybirdInvoice(created.Id, created.Reference);
    }

    /// <summary>
    /// Stable, URL-safe customer id for an e-mail address. Dots are avoided because Moneybird
    /// reads them as a format extension in the customer_id lookup URL.
    /// </summary>
    public static string CustomerIdFor(string email)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()));
        return "mockshop-" + Convert.ToHexStringLower(hash)[..16];
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken,
        bool notFoundIsNull = false)
    {
        if (!settings.IsConfigured)
        {
            throw new MoneybirdException("Moneybird is niet geconfigureerd (token of administratie-ID ontbreekt).");
        }

        using var request = new HttpRequestMessage(method, BuildUri(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new MoneybirdException($"Moneybird is niet bereikbaar: {ex.Message}", inner: ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MoneybirdException("Moneybird reageerde niet op tijd (timeout).", inner: ex);
        }

        using (response)
        {
            if (notFoundIsNull && response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await ToExceptionAsync(response, cancellationToken);
            }

            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
        }
    }

    private Uri BuildUri(string path)
    {
        var baseUrl = settings.BaseUrl.EndsWith('/') ? settings.BaseUrl : settings.BaseUrl + "/";
        return new Uri(new Uri(baseUrl), $"{Uri.EscapeDataString(settings.AdministrationId!)}/{path}");
    }

    private static async Task<MoneybirdException> ToExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = response.StatusCode;
        var message = status switch
        {
            HttpStatusCode.Unauthorized => "Moneybird-token is ongeldig of verlopen (401).",
            HttpStatusCode.Forbidden => "Moneybird-token heeft geen rechten voor deze actie (403).",
            HttpStatusCode.NotFound => "Moneybird-administratie of resource niet gevonden (404). Controleer de administratie-ID.",
            HttpStatusCode.PaymentRequired => "Moneybird sandbox-factuurlimiet bereikt (402).",
            HttpStatusCode.TooManyRequests => "Moneybird rate limit bereikt (429). Later opnieuw proberen.",
            HttpStatusCode.UnprocessableEntity => $"Moneybird weigerde de gegevens (422): {await ReadErrorAsync(response, cancellationToken)}",
            _ => $"Moneybird gaf foutcode {(int)status}: {await ReadErrorAsync(response, cancellationToken)}"
        };
        return new MoneybirdException(message, status);
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        if (text.Length == 0)
        {
            return response.ReasonPhrase ?? "onbekende fout";
        }
        return text.Length <= MaxErrorBodyLength ? text : text[..MaxErrorBodyLength] + "…";
    }

    private static MoneybirdContact ToContact(ContactDto c) => new(c.Id, c.CustomerId);

    public async Task<MoneybirdInvoiceDetails> GetInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default)
    {
        var dto = await SendAsync<InvoiceDetailDto>(HttpMethod.Get, $"sales_invoices/{Uri.EscapeDataString(invoiceId)}.json", null, cancellationToken)
            ?? throw new MoneybirdException("Moneybird gaf een leeg antwoord bij het ophalen van de factuur.");
        return new MoneybirdInvoiceDetails(dto.Id, dto.InvoiceId, dto.State, dto.CreatedAt);
    }

    private sealed record ContactDto(string Id, string? CustomerId);

    private sealed record InvoiceDto(string Id, string? Reference);

    private sealed record InvoiceDetailDto(string Id, string? InvoiceId, string State, DateTimeOffset CreatedAt);
}
