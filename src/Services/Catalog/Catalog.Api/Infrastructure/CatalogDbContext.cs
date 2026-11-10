using BuildingBlocks.Messaging;
using Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Infrastructure;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : DbContext(options), IMessagingDbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddMessagingTables();

        modelBuilder.Entity<Product>(b =>
        {
            b.ToTable("Products");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(160).IsRequired();
            b.Property(x => x.Description).HasMaxLength(600);
            b.Property(x => x.Category).HasMaxLength(80).IsRequired();
            b.Property(x => x.Price).HasPrecision(18, 2);
            b.Property(x => x.ImageUrl).HasMaxLength(400);

            // Filtered + covering index: yalnızca aktif ürünleri kategoriye göre hızlı listelemek için
            b.HasIndex(x => new { x.Category, x.Name })
             .HasFilter("[IsActive] = 1")
             .IncludeProperties(x => new { x.Price, x.Stock, x.ImageUrl, x.Description })
             .HasDatabaseName("IX_Products_Active_Category_Name");
        });

        modelBuilder.Entity<StockReservation>(b =>
        {
            b.ToTable("StockReservations");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.OrderId).HasDatabaseName("IX_StockReservations_OrderId");
        });
    }
}

