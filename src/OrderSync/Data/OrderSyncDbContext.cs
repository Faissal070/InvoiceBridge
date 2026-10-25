using Microsoft.EntityFrameworkCore;
using OrderSync.Models;

namespace OrderSync.Data;

public class OrderSyncDbContext(DbContextOptions<OrderSyncDbContext> options) : DbContext(options)
{
    public DbSet<SyncedOrder> SyncedOrders => Set<SyncedOrder>();

    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SyncedOrder>(entity =>
        {
            entity.HasIndex(o => o.OrderId).IsUnique();
            entity.Property(o => o.OrderId).HasMaxLength(50);
            entity.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(o => o.MoneybirdInvoiceId).HasMaxLength(50);
            entity.Property(o => o.CustomerName).HasMaxLength(200);
            entity.HasMany(o => o.InvoiceLines)
                .WithOne()
                .HasForeignKey(l => l.SyncedOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InvoiceLine>(entity =>
        {
            entity.Property(l => l.Description).HasMaxLength(200);
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite cannot sort or compare DateTimeOffset natively; store as UTC ticks.
        configurationBuilder.Properties<DateTimeOffset>()
            .HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
    }
}
