namespace OrderSync.Services;

/// <summary>
/// Identifies the current visitor for the demo limits: the IP address, or a random id per connection
/// if that is unknown. Filled in by <see cref="Components.VisitorCircuitHandler"/>.
/// </summary>
public class VisitorContext
{
    public string VisitorId { get; set; } = Guid.NewGuid().ToString("N");
}
