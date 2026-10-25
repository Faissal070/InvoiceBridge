namespace OrderSync.Services;

/// <summary>
/// Tells open pages that order states changed, so every visitor sees updates right away.
/// </summary>
public class OrderChangeNotifier
{
    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();
}
