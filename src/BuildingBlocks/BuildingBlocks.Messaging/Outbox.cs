using System.Diagnostics;
using System.Text.Json;
using BuildingBlocks.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Transactional Outbox kaydı: iş verisiyle AYNI transaction içinde yazılır,
/// <see cref="OutboxPublisher{TDbContext}"/> tarafından sonradan RabbitMQ'ya iletilir.
/// Böylece "veritabanı güncellendi ama mesaj gönderilemedi" tutarsızlığı oluşmaz.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
    public string? TraceParent { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// Inbox kaydı: işlenmiş mesajların kimliği tutulur. RabbitMQ "at-least-once" teslim garantisi verdiği
/// için aynı mesaj birden fazla gelebilir; bu tablo tüketiciyi idempotent yapar.
/// </summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; set; }
    public string Type { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
}

public interface IMessagingDbContext
{
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<InboxMessage> InboxMessages { get; }
}

public static class MessagingJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public static class MessagingModelBuilderExtensions
{
    public static ModelBuilder AddMessagingTables(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("OutboxMessages");
            b.HasKey(x => x.Id);
            b.Property(x => x.Type).HasMaxLength(200).IsRequired();
            b.Property(x => x.CorrelationId).HasMaxLength(64);
            b.Property(x => x.TraceParent).HasMaxLength(128);
            b.Property(x => x.LastError).HasMaxLength(2000);
            // Filtered index: yayınlanmayı bekleyen az sayıdaki satırı hızlıca bulmak için
            b.HasIndex(x => x.OccurredAt)
             .HasFilter("[ProcessedAt] IS NULL")
             .HasDatabaseName("IX_OutboxMessages_Unprocessed");
        });

        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable("InboxMessages");
            b.HasKey(x => x.MessageId);
            b.Property(x => x.Type).HasMaxLength(200).IsRequired();
        });

        return modelBuilder;
    }
}

public static class OutboxExtensions
{
    /// <summary>
    /// Mesajı outbox'a ekler. SaveChanges çağrılana kadar hiçbir şey gönderilmez;
    /// iş verisi ile birlikte atomik olarak kaydedilir.
    /// </summary>
    public static void AddOutboxMessage<T>(this IMessagingDbContext db, T message) where T : IIntegrationMessage
    {
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = typeof(T).Name,
            Payload = JsonSerializer.Serialize(message, MessagingJson.Options),
            CorrelationId = CorrelationContext.CorrelationId,
            TraceParent = Activity.Current?.Id,
            OccurredAt = DateTime.UtcNow
        });
    }
}

