namespace OrderSync.Exceptions;

/// <summary>
/// The webshop does not know the order (anymore). The message is safe to show on the dashboard.
/// </summary>
public class OrderNotFoundException(string orderId)
    : Exception($"Bestelling {orderId} is niet gevonden in de webshop.")
{
    public string OrderId { get; } = orderId;
}
