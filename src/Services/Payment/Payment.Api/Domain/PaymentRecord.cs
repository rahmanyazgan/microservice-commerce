using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Payment.Api.Domain;

public enum PaymentStatus
{
    AwaitingCard,
    ThreeDsPending,
    Approved,
    Declined,
    Failed,
    Cancelled,
    Reversed
}

public sealed class PaymentRecord
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "TRY";

    public string MaskedPan { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public int ExpiryMonth { get; set; }
    public int ExpiryYear { get; set; }

    /// <summary>3D Secure OTP tamamlandığında ISO 8583 kararını verebilmek için tutulan kart sonucu ipucu.</summary>
    public string? IssuerDecisionKey { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.AwaitingCard;
    public string? IdempotencyKey { get; set; }

    // 3D Secure alanları
    public string? ThreeDsChallengeId { get; set; }
    public int ThreeDsRemainingAttempts { get; set; } = 3;
    public string? Eci { get; set; }

    // ISO 8583 alanları
    public string? Stan { get; set; }
    public string? Rrn { get; set; }
    public string? AuthCode { get; set; }
    public string? ResponseCode { get; set; }
    public string? ResponseText { get; set; }
    public string? IsoRequestRaw { get; set; }
    public string? IsoResponseRaw { get; set; }
    public string? IsoReversalRaw { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options)
    : DbContext(options), IMessagingDbContext
{
    public DbSet<PaymentRecord> Payments => Set<PaymentRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddMessagingTables();

        modelBuilder.Entity<PaymentRecord>(b =>
        {
            b.ToTable("Payments");
            b.HasKey(x => x.Id);
            b.Property(x => x.CustomerId).HasMaxLength(80);
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.Property(x => x.Currency).HasMaxLength(8);
            b.Property(x => x.MaskedPan).HasMaxLength(32);
            b.Property(x => x.Brand).HasMaxLength(32);
            b.Property(x => x.IssuerDecisionKey).HasMaxLength(32);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            b.Property(x => x.IdempotencyKey).HasMaxLength(100);
            b.Property(x => x.ThreeDsChallengeId).HasMaxLength(64);
            b.Property(x => x.Eci).HasMaxLength(8);
            b.Property(x => x.Stan).HasMaxLength(12);
            b.Property(x => x.Rrn).HasMaxLength(20);
            b.Property(x => x.AuthCode).HasMaxLength(12);
            b.Property(x => x.ResponseCode).HasMaxLength(8);
            b.Property(x => x.ResponseText).HasMaxLength(200);
            b.Property(x => x.IsoRequestRaw).HasMaxLength(1000);
            b.Property(x => x.IsoResponseRaw).HasMaxLength(1000);
            b.Property(x => x.IsoReversalRaw).HasMaxLength(1000);

            b.HasIndex(x => x.OrderId).IsUnique();
            b.HasIndex(x => x.IdempotencyKey)
             .IsUnique()
             .HasFilter("[IdempotencyKey] IS NOT NULL");
        });
    }
}

