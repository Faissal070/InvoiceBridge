using Microsoft.Extensions.Logging.Abstractions;
using InvoiceBridge.Services;

namespace InvoiceBridge.Tests.Services;

public sealed class SandboxSimulationServiceTests
{
    private readonly SandboxSimulationService simulation = new(NullLogger<SandboxSimulationService>.Instance);

    [Fact]
    public void Not_configured_switches_to_simulation()
    {
        simulation.ActivateNotConfigured();

        Assert.True(simulation.IsSimulating);
        Assert.Equal(SimulationReason.NotConfigured, simulation.Reason);
    }

    [Fact]
    public void Not_configured_has_no_hourly_retry()
    {
        simulation.ActivateNotConfigured();

        Assert.False(simulation.ShouldRetryMoneybird);
    }

    [Fact]
    public void Local_limit_does_not_replace_not_configured()
    {
        simulation.ActivateNotConfigured();
        simulation.ActivateLocalLimit();

        Assert.Equal(SimulationReason.NotConfigured, simulation.Reason);
    }

    [Fact]
    public void Local_limit_does_not_replace_payment_required()
    {
        simulation.ActivatePaymentRequired();
        simulation.ActivateLocalLimit();

        Assert.Equal(SimulationReason.PaymentRequired, simulation.Reason);
    }
}
