namespace InvoiceBridge.Services;

public enum SimulationReason { None, LocalLimit, PaymentRequired, NotConfigured }

/// <summary>
/// Singleton that tracks whether the app has switched to simulation mode because the Moneybird
/// sandbox invoice limit was reached, Moneybird returned 402, or Moneybird is not configured at all.
/// </summary>
public sealed class SandboxSimulationService(ILogger<SandboxSimulationService> logger)
{
    private readonly object _lock = new();
    private SimulationReason _reason;
    private DateTime _since;

    public bool IsSimulating { get { lock (_lock) return _reason != SimulationReason.None; } }

    public SimulationReason Reason { get { lock (_lock) return _reason; } }

    /// <summary>True when a 402-triggered simulation has been running for at least an hour and a retry is due.</summary>
    public bool ShouldRetryMoneybird
    {
        get
        {
            lock (_lock)
            return _reason == SimulationReason.PaymentRequired &&
                   DateTime.UtcNow - _since >= TimeSpan.FromHours(1);
        }
    }

    /// <summary>Switches to simulation because the local monthly invoice count reached the configured limit.</summary>
    public void ActivateLocalLimit()
    {
        lock (_lock)
        {
            if (_reason == SimulationReason.None)
                logger.LogWarning("Sandbox-factuurlimiet bereikt; overschakelen naar simulatiemodus (lokale teller).");
            // Don't downgrade from PaymentRequired or NotConfigured to LocalLimit.
            if (_reason == SimulationReason.None)
                _reason = SimulationReason.LocalLimit;
        }
    }

    /// <summary>
    /// Switches to simulation because there are no Moneybird secrets. Permanent for this process:
    /// there is no hourly retry, because the configuration only changes on a restart.
    /// </summary>
    public void ActivateNotConfigured()
    {
        lock (_lock)
        {
            if (_reason != SimulationReason.NotConfigured)
                logger.LogWarning("Geen Moneybird-configuratie gevonden; de app draait in simulatiemodus en facturen worden lokaal gesimuleerd.");
            _reason = SimulationReason.NotConfigured;
        }
    }

    /// <summary>Switches to (or stays in) simulation because Moneybird returned 402. Resets the hourly retry timer.</summary>
    public void ActivatePaymentRequired()
    {
        lock (_lock)
        {
            if (_reason != SimulationReason.PaymentRequired)
                logger.LogWarning("Moneybird geeft 402; overschakelen naar simulatiemodus.");
            _reason = SimulationReason.PaymentRequired;
            _since = DateTime.UtcNow;
        }
    }

    /// <summary>Returns to normal Moneybird mode. Called after a successful Moneybird call following a retry.</summary>
    public void Deactivate()
    {
        lock (_lock)
        {
            if (_reason != SimulationReason.None)
                logger.LogInformation("Simulatiemodus uitgeschakeld; teruggeschakeld naar Moneybird.");
            _reason = SimulationReason.None;
            _since = default;
        }
    }
}
