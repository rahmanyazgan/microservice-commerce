using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Outbox tablosundaki yayınlanmamış mesajları periyodik olarak okuyup RabbitMQ'ya iletir.
/// Publisher confirms açıktır: broker mesajı aldığını onaylamadan kayıt "işlendi" sayılmaz.
/// Not: Birden fazla instance çalıştırılacaksa satırlar UPDLOCK/READPAST ile kilitlenmelidir.
/// </summary>
public sealed class OutboxPublisher<TDbContext>(
    IServiceScopeFactory scopeFactory,
    RabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    ILogger<OutboxPublisher<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext, IMessagingDbContext
{
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        IChannel? channel = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (channel is not { IsOpen: true })
                {
                    var conn = await connection.GetConnectionAsync(stoppingToken);
                    channel = await conn.CreateChannelAsync(
                        new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                        stoppingToken);
                    await channel.ExchangeDeclareAsync(o.Exchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
                }

                var published = await PublishBatchAsync(channel, o.Exchange, stoppingToken);
                if (published < BatchSize)
                    await Task.Delay(o.OutboxPollingIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox yayınlama döngüsünde hata");
                await Task.Delay(2000, stoppingToken);
            }
        }

        if (channel is not null) await channel.DisposeAsync();
    }

    private async Task<int> PublishBatchAsync(IChannel channel, string exchange, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var messages = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.Attempts < 10)
            .OrderBy(m => m.OccurredAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var message in messages)
        {
            ActivityContext.TryParse(message.TraceParent, null, out var parent);
            using var activity = MessagingDiagnostics.ActivitySource.StartActivity(
                $"{message.Type} publish", ActivityKind.Producer, parent);
            activity?.SetTag("messaging.system", "rabbitmq");
            activity?.SetTag("messaging.destination.name", exchange);
            activity?.SetTag("messaging.message.id", message.Id);

            try
            {
                var props = new BasicProperties
                {
                    MessageId = message.Id.ToString(),
                    Type = message.Type,
                    CorrelationId = message.CorrelationId,
                    ContentType = "application/json",
                    DeliveryMode = DeliveryModes.Persistent,
                    Timestamp = new AmqpTimestamp(new DateTimeOffset(message.OccurredAt).ToUnixTimeSeconds()),
                    Headers = new Dictionary<string, object?>
                    {
                        [MessagingDiagnostics.TraceParentHeader] = activity?.Id ?? message.TraceParent
                    }
                };

                await channel.BasicPublishAsync(exchange, routingKey: message.Type, mandatory: false,
                    basicProperties: props, body: Encoding.UTF8.GetBytes(message.Payload), cancellationToken: ct);

                message.ProcessedAt = DateTime.UtcNow;
                logger.LogDebug("Outbox mesajı yayınlandı: {Type} {Id}", message.Type, message.Id);
            }
            catch (Exception ex)
            {
                message.Attempts++;
                message.LastError = ex.Message;
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                logger.LogWarning(ex, "Outbox mesajı yayınlanamadı: {Type} {Id}", message.Type, message.Id);
                break; // sıralamayı korumak için bu turu bitir
            }
        }

        if (messages.Count > 0)
            await db.SaveChangesAsync(ct);

        return messages.Count;
    }
}

