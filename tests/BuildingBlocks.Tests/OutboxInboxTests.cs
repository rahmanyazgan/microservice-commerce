using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BuildingBlocks.Tests;

public sealed class OutboxInboxTests
{
    private sealed class TestMessagingDbContext(DbContextOptions<TestMessagingDbContext> options)
        : DbContext(options), IMessagingDbContext
    {
        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
        public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.AddMessagingTables();
    }

    [Fact]
    public async Task InboxProcessor_Ayni_MessageId_Ile_Ikinci_Cagriyi_Atlamali()
    {
        await using var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<TestMessagingDbContext>(o => o.UseSqlite(conn));
        await using var sp = services.BuildServiceProvider();

        using (var initScope = sp.CreateScope())
        {
            await initScope.ServiceProvider.GetRequiredService<TestMessagingDbContext>().Database.EnsureCreatedAsync();
        }

        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var msgId = Guid.NewGuid();
        var invocationCount = 0;

        var first = await InboxProcessor.ExecuteIdempotentAsync<TestMessagingDbContext>(
            scopeFactory, msgId, nameof(OrderConfirmed),
            _ => { invocationCount++; return Task.CompletedTask; },
            NullLogger.Instance);

        var second = await InboxProcessor.ExecuteIdempotentAsync<TestMessagingDbContext>(
            scopeFactory, msgId, nameof(OrderConfirmed),
            _ => { invocationCount++; return Task.CompletedTask; },
            NullLogger.Instance);

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public async Task AddOutboxMessage_CorrelationId_Ve_Payload_Kaydetmeli()
    {
        await using var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();

        var options = new DbContextOptionsBuilder<TestMessagingDbContext>().UseSqlite(conn).Options;
        await using var db = new TestMessagingDbContext(options);
        await db.Database.EnsureCreatedAsync();

        CorrelationContext.CorrelationId = "kor-test-123";
        var evt = new OrderConfirmed(Guid.NewGuid(), "demo-musteri", 1250m);
        db.AddOutboxMessage(evt);
        await db.SaveChangesAsync();

        var saved = await db.OutboxMessages.SingleAsync();
        Assert.Equal(nameof(OrderConfirmed), saved.Type);
        Assert.Equal("kor-test-123", saved.CorrelationId);
        Assert.Contains("demo-musteri", saved.Payload);
        Assert.Null(saved.ProcessedAt);
    }
}

