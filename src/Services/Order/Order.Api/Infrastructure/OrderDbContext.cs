using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Order.Api.Domain;

namespace Order.Api.Infrastructure;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options)
    : DbContext(options), IMessagingDbContext
{
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderHistoryEntry> OrderHistory => Set<OrderHistoryEntry>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddMessagingTables();

        modelBuilder.Entity<OrderEntity>(b =>
        {
            b.ToTable("Orders");
            b.HasKey(x => x.Id);
            b.Property(x => x.CustomerId).HasMaxLength(80).IsRequired();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            b.Property(x => x.TotalAmount).HasPrecision(18, 2);
            b.Property(x => x.CancelReason).HasMaxLength(400);
            b.Ignore(x => x.StatusText);

            // Covering index: müşteri siparişlerini tarihe göre sıralarken Key Lookup maliyetini önler
            b.HasIndex(x => new { x.CustomerId, x.CreatedAt })
             .IsDescending(false, true)
             .IncludeProperties(x => new { x.Status, x.TotalAmount, x.CancelReason, x.PaymentId })
             .HasDatabaseName("IX_Orders_CustomerId_CreatedAt_Covering");

            b.HasIndex(x => new { x.Status, x.CreatedAt })
             .HasDatabaseName("IX_Orders_Status_CreatedAt");

            b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.History).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(b =>
        {
            b.ToTable("OrderItems");
            b.HasKey(x => x.Id);
            b.Property(x => x.ProductName).HasMaxLength(160).IsRequired();
            b.Property(x => x.UnitPrice).HasPrecision(18, 2);
            b.HasIndex(x => x.OrderId);
        });

        modelBuilder.Entity<OrderHistoryEntry>(b =>
        {
            b.ToTable("OrderHistory");
            b.HasKey(x => x.Id);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            b.Property(x => x.Note).HasMaxLength(500).IsRequired();
            b.HasIndex(x => x.OrderId);
        });
    }
}

