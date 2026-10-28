using System.Net;

namespace InvoiceBridge.Exceptions;

/// <summary>
/// A Moneybird API error with a message that is safe to show on the dashboard.
/// </summary>
public class MoneybirdException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
    : Exception(message, inner)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;

    public bool IsRateLimited => StatusCode == HttpStatusCode.TooManyRequests;

    public bool IsPaymentRequired => StatusCode == HttpStatusCode.PaymentRequired;
}
