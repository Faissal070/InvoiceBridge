using OrderSync.Services;

namespace OrderSync.Workers;

/// <summary>
/// Periodically polls the webshop for new orders.
/// </summary>
public class OrderSyncWorker(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    IConfiguration configuration,
    ILogger<OrderSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The mock shop is hosted in this same app, so wait until it accepts requests.
        if (!await WaitForAppStartedAsync(stoppingToken))
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(configuration.GetValue("Sync:IntervalMinutes", 2));
        logger.LogInformation("Order sync worker started, interval {Interval}", interval);

        using var timer = new PeriodicTimer(interval);
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();

        // Fetching and invoicing are independent: pending orders are still invoiced when the shop is down.
        await TryAsync("Fetching new orders", () =>
            scope.ServiceProvider.GetRequiredService<OrderSyncService>().SyncNewOrdersAsync(stoppingToken), stoppingToken);
        await TryAsync("Invoicing orders", () =>
            scope.ServiceProvider.GetRequiredService<OrderInvoicingService>().InvoicePendingOrdersAsync(stoppingToken), stoppingToken);
    }

    private async Task TryAsync(string step, Func<Task> action, CancellationToken stoppingToken)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Step} failed", step);
        }
    }

    private async Task<bool> WaitForAppStartedAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource();
        using var _ = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        using var __ = stoppingToken.Register(() => started.TrySetCanceled());
        try
        {
            await started.Task;
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
