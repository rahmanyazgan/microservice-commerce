using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using Notification.Worker;

var builder = WebApplication.CreateBuilder(args);
builder.AddStandardServiceDefaults("Notification.Worker");

var connStr = builder.Configuration.GetConnectionString("NotificationDb")
              ?? "Server=(localdb)\\MSSQLLocalDB;Database=ETicaret_NotificationDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

builder.Services.AddDbContext<NotificationDbContext>(o => o.UseSqlServer(connStr));

builder.Services.AddRabbitMqMessaging<NotificationDbContext>(builder.Configuration, queueName: "notification")
    .AddHandler<OrderConfirmed, NotificationHandlers>()
    .AddHandler<OrderCancelled, NotificationHandlers>();

var app = builder.Build();
app.UseStandardServicePipeline();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.MapGet("/api/notifications", async (string? customerId, NotificationDbContext db, CancellationToken ct) =>
{
    var cid = string.IsNullOrWhiteSpace(customerId) ? "demo-musteri" : customerId.Trim();
    var list = await db.Notifications.AsNoTracking()
        .Where(n => n.CustomerId == cid)
        .OrderByDescending(n => n.CreatedAt)
        .Take(50)
        .ToListAsync(ct);

    return Results.Ok(list);
});

app.Run();

namespace Notification.Worker
{
    public sealed class NotificationEntry
    {
        public int Id { get; set; }
        public Guid OrderId { get; set; }
        public string CustomerId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options)
        : DbContext(options), IMessagingDbContext
    {
        public DbSet<NotificationEntry> Notifications => Set<NotificationEntry>();
        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
        public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.AddMessagingTables();
            modelBuilder.Entity<NotificationEntry>(b =>
            {
                b.ToTable("Notifications");
                b.HasKey(x => x.Id);
                b.Property(x => x.CustomerId).HasMaxLength(80).IsRequired();
                b.Property(x => x.Type).HasMaxLength(40).IsRequired();
                b.Property(x => x.Title).HasMaxLength(160).IsRequired();
                b.Property(x => x.Message).HasMaxLength(600).IsRequired();
                b.HasIndex(x => new { x.CustomerId, x.CreatedAt }).IsDescending(false, true);
            });
        }
    }

    public sealed class NotificationHandlers(NotificationDbContext db, ILogger<NotificationHandlers> logger)
        : IMessageHandler<OrderConfirmed>,
          IMessageHandler<OrderCancelled>
    {
        public Task HandleAsync(OrderConfirmed message, CancellationToken ct)
        {
            var shortId = message.OrderId.ToString("N")[..8].ToUpperInvariant();
            db.Notifications.Add(new NotificationEntry
            {
                OrderId = message.OrderId,
                CustomerId = message.CustomerId,
                Type = nameof(OrderConfirmed),
                Title = $"Siparişiniz Onaylandı (#{shortId})",
                Message = $"#{shortId} numaralı siparişiniz için {message.TotalAmount:N2} ₺ tutarındaki ödemeniz alındı ve hazırlanmaya başlandı.",
                CreatedAt = DateTime.UtcNow
            });

            logger.LogInformation("BİLDİRİM [Onay]: Müşteri={Customer}, Sipariş={OrderId}", message.CustomerId, message.OrderId);
            return Task.CompletedTask;
        }

        public Task HandleAsync(OrderCancelled message, CancellationToken ct)
        {
            var shortId = message.OrderId.ToString("N")[..8].ToUpperInvariant();
            db.Notifications.Add(new NotificationEntry
            {
                OrderId = message.OrderId,
                CustomerId = message.CustomerId,
                Type = nameof(OrderCancelled),
                Title = $"Siparişiniz İptal Edildi (#{shortId})",
                Message = $"#{shortId} numaralı siparişiniz iptal edildi. Neden: {message.Reason}",
                CreatedAt = DateTime.UtcNow
            });

            logger.LogInformation("BİLDİRİM [İptal]: Müşteri={Customer}, Sipariş={OrderId}, Neden={Reason}",
                message.CustomerId, message.OrderId, message.Reason);
            return Task.CompletedTask;
        }
    }
}

