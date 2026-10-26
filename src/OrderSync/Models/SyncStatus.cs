namespace OrderSync.Models;

public enum SyncStatus
{
    Pending,
    Processing,
    Synced,
    Failed,
    Simulated
}
