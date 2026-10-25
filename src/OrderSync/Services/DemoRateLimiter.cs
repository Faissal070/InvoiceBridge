using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using OrderSync.Options;

namespace OrderSync.Services;

/// <summary>
/// Per-visitor limits for the demo buttons. Kept in memory; the global limits live in the database.
/// </summary>
public sealed class DemoRateLimiter(IOptions<DemoOptions> options) : IDisposable
{
    private readonly PartitionedRateLimiter<string> orders = PartitionedRateLimiter.Create<string, string>(visitor =>
        RateLimitPartition.GetSlidingWindowLimiter(visitor, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = options.Value.MaxOrdersPerVisitor,
            Window = TimeSpan.FromMinutes(options.Value.VisitorWindowMinutes),
            SegmentsPerWindow = 10,
            QueueLimit = 0
        }));

    private readonly PartitionedRateLimiter<string> retries = PartitionedRateLimiter.Create<string, string>(visitor =>
        RateLimitPartition.GetFixedWindowLimiter(visitor, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromSeconds(options.Value.RetryCooldownSeconds),
            QueueLimit = 0
        }));

    /// <param name="retryAfter">When refused: roughly how long until the visitor can place an order again.</param>
    public bool TryAcquireOrder(string visitorId, out TimeSpan retryAfter)
    {
        using var lease = orders.AttemptAcquire(visitorId);
        if (lease.IsAcquired)
        {
            retryAfter = TimeSpan.Zero;
            return true;
        }

        retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
            ? wait
            : TimeSpan.FromMinutes(options.Value.VisitorWindowMinutes);
        return false;
    }

    public bool TryAcquireRetry(string visitorId)
    {
        using var lease = retries.AttemptAcquire(visitorId);
        return lease.IsAcquired;
    }

    public void Dispose()
    {
        orders.Dispose();
        retries.Dispose();
    }
}
