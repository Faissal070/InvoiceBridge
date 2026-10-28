using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using InvoiceBridge.Clients;
using InvoiceBridge.Exceptions;
using InvoiceBridge.Models;
using InvoiceBridge.Options;

namespace InvoiceBridge.Tests.Clients;

public class MoneybirdClientTests
{
    private const string Token = "test-token-geheim";
    private const string AdministrationId = "123456";

    private readonly RecordingHandler handler = new();

    [Fact]
    public async Task Requests_use_administration_url_and_bearer_token()
    {
        handler.Respond(HttpStatusCode.OK, """{"id":"1","customer_id":"x"}""");

        await CreateClient().FindContactByEmailAsync("sanne@example.nl");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        var customerId = MoneybirdClient.CustomerIdFor("sanne@example.nl");
        Assert.Equal($"https://moneybird.test/api/v2/{AdministrationId}/contacts/customer_id/{customerId}.json", request.Uri.ToString());
        Assert.Equal($"Bearer {Token}", request.Authorization);
    }

    [Fact]
    public void CustomerId_is_stable_case_insensitive_and_url_safe()
    {
        var id = MoneybirdClient.CustomerIdFor("Sanne.DeVries@Example.nl ");

        Assert.Equal(id, MoneybirdClient.CustomerIdFor("sanne.devries@example.nl"));
        Assert.NotEqual(id, MoneybirdClient.CustomerIdFor("daan.jansen@example.nl"));
        Assert.Matches("^mockshop-[0-9a-f]{16}$", id);
    }

    [Fact]
    public async Task FindInvoiceByReference_filters_on_reference_over_all_states()
    {
        handler.Respond(HttpStatusCode.OK, """[{"id":"9001","reference":"WS-20261001"}]""");

        var invoice = await CreateClient().FindInvoiceByReferenceAsync("WS-20261001");

        Assert.Equal("9001", invoice?.Id);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/api/v2/123456/sales_invoices.json", request.Uri.AbsolutePath);
        Assert.Equal("reference:WS-20261001,state:all", Uri.UnescapeDataString(request.Uri.Query.Replace("?filter=", "")));
    }

    [Fact]
    public async Task FindInvoiceByReference_ignores_invoices_with_other_reference()
    {
        handler.Respond(HttpStatusCode.OK, """[{"id":"9001","reference":"WS-20261001-B"}]""");

        Assert.Null(await CreateClient().FindInvoiceByReferenceAsync("WS-20261001"));
    }

    [Fact]
    public async Task FindContactByEmail_returns_null_when_customer_id_unknown()
    {
        handler.Respond(HttpStatusCode.NotFound, """{"error":"record not found"}""");

        Assert.Null(await CreateClient().FindContactByEmailAsync("sanne@example.nl"));
    }

    [Fact]
    public async Task CreateContact_posts_name_customer_id_and_manual_delivery_without_email()
    {
        var customerId = MoneybirdClient.CustomerIdFor("sanne@example.nl");
        handler.Respond(HttpStatusCode.Created, $$"""{"id":"77","customer_id":"{{customerId}}"}""");

        var contact = await CreateClient().CreateContactAsync(new NewMoneybirdContact("Sanne", "de Vries", "sanne@example.nl"));

        Assert.Equal(new MoneybirdContact("77", customerId), contact);
        var body = handler.Requests.Single().Json!.Value.GetProperty("contact");
        Assert.Equal("Sanne", body.GetProperty("firstname").GetString());
        Assert.Equal("de Vries", body.GetProperty("lastname").GetString());
        Assert.Equal(customerId, body.GetProperty("customer_id").GetString());
        Assert.Equal("Manual", body.GetProperty("delivery_method").GetString());
        // Moneybird rejects the fake demo e-mail domains, so no e-mail fields are sent.
        Assert.False(body.TryGetProperty("email", out _));
        Assert.False(body.TryGetProperty("send_invoices_to_email", out _));
    }

    [Fact]
    public async Task CreateInvoice_posts_reference_date_and_lines()
    {
        handler.Respond(HttpStatusCode.Created, """{"id":"9002","reference":"WS-1"}""");

        var invoice = await CreateClient().CreateInvoiceAsync(new NewMoneybirdInvoice(
            "77", "WS-1", new DateOnly(2026, 10, 3),
            [new NewMoneybirdInvoiceLine("Koffiebonen 1 kg", 2, 24.95m)]));

        Assert.Equal("9002", invoice.Id);
        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v2/123456/sales_invoices.json", request.Uri.AbsolutePath);

        var body = request.Json!.Value.GetProperty("sales_invoice");
        Assert.Equal("77", body.GetProperty("contact_id").GetString());
        Assert.Equal("WS-1", body.GetProperty("reference").GetString());
        Assert.Equal("2026-10-03", body.GetProperty("invoice_date").GetString());
        Assert.True(body.GetProperty("prices_are_incl_tax").GetBoolean());
        var line = Assert.Single(body.GetProperty("details_attributes").EnumerateArray());
        Assert.Equal("Koffiebonen 1 kg", line.GetProperty("description").GetString());
        Assert.Equal("2", line.GetProperty("amount").GetString());
        Assert.Equal("24.95", line.GetProperty("price").GetString());
        Assert.False(line.TryGetProperty("tax_rate_id", out _));
    }

    [Fact]
    public async Task CreateInvoice_includes_configured_tax_rate()
    {
        handler.Respond(HttpStatusCode.Created, """{"id":"9002","reference":"WS-1"}""");

        await CreateClient(taxRateId: "555").CreateInvoiceAsync(new NewMoneybirdInvoice(
            "77", "WS-1", new DateOnly(2026, 10, 3), [new NewMoneybirdInvoiceLine("A", 1, 1m)]));

        var line = handler.Requests.Single().Json!.Value.GetProperty("sales_invoice").GetProperty("details_attributes")[0];
        Assert.Equal("555", line.GetProperty("tax_rate_id").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "token is ongeldig")]
    [InlineData(HttpStatusCode.NotFound, "administratie")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate limit")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "contact_id is required")]
    [InlineData(HttpStatusCode.InternalServerError, "foutcode 500")]
    public async Task Errors_are_translated_to_readable_messages(HttpStatusCode status, string expected)
    {
        handler.Respond(status, """{"error":{"contact_id":["contact_id is required"]}}""");

        var ex = await Assert.ThrowsAsync<MoneybirdException>(() => CreateClient().FindInvoiceByReferenceAsync("WS-1"));

        Assert.Contains(expected, ex.Message);
        Assert.Equal(status, ex.StatusCode);
        Assert.Equal(status == HttpStatusCode.TooManyRequests, ex.IsRateLimited);
        Assert.DoesNotContain(Token, ex.Message);
    }

    [Fact]
    public async Task Network_failure_becomes_MoneybirdException_without_token()
    {
        handler.Throw(new HttpRequestException("Connection refused"));

        var ex = await Assert.ThrowsAsync<MoneybirdException>(() => CreateClient().FindInvoiceByReferenceAsync("WS-1"));

        Assert.Contains("niet bereikbaar", ex.Message);
        Assert.DoesNotContain(Token, ex.Message);
    }

    [Fact]
    public async Task Unconfigured_client_throws_and_sends_nothing()
    {
        var client = new MoneybirdClient(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(new MoneybirdOptions()));

        Assert.False(client.IsConfigured);
        await Assert.ThrowsAsync<MoneybirdException>(() => client.FindInvoiceByReferenceAsync("WS-1"));
        Assert.Empty(handler.Requests);
    }

    private MoneybirdClient CreateClient(string? taxRateId = null) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(new MoneybirdOptions
        {
            BaseUrl = "https://moneybird.test/api/v2",
            Token = Token,
            AdministrationId = AdministrationId,
            TaxRateId = taxRateId
        }));

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, JsonElement? Json);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private HttpStatusCode status = HttpStatusCode.OK;
        private string body = "";
        private Exception? exception;

        public List<RecordedRequest> Requests { get; } = [];

        public void Respond(HttpStatusCode statusCode, string responseBody) => (status, body) = (statusCode, responseBody);

        public void Throw(Exception ex) => exception = ex;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            JsonElement? json = null;
            if (request.Content is not null)
            {
                json = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
            }
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), json));

            if (exception is not null) throw exception;
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
