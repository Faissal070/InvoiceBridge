using Microsoft.AspNetCore.Components.Server.Circuits;
using OrderSync.Services;

namespace OrderSync.Components;

/// <summary>
/// Records the visitor's IP address when an interactive connection opens. The HTTP context is only
/// reliable at that moment, not later on when buttons are clicked.
/// </summary>
public class VisitorCircuitHandler(IHttpContextAccessor httpContextAccessor, VisitorContext visitor) : CircuitHandler
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        if (httpContextAccessor.HttpContext?.Connection.RemoteIpAddress is { } ip)
        {
            visitor.VisitorId = ip.MapToIPv6().ToString();
        }
        return Task.CompletedTask;
    }
}
