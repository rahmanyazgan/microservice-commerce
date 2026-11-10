using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BuildingBlocks.Messaging;

public interface IMessageHandler<in TMessage> where TMessage : IIntegrationMessage
{
    Task HandleAsync(TMessage message, CancellationToken ct);
}

public sealed class MessageHandlerRegistry
{
    private readonly Dictionary<string, (Type MessageType, Type HandlerInterfaceType)> _handlers = new(StringComparer.OrdinalIgnoreCase);

    public void Register<TMessage>() where TMessage : IIntegrationMessage
    {
        _handlers[typeof(TMessage).Name] = (typeof(TMessage), typeof(IMessageHandler<TMessage>));
    }

    public IReadOnlyCollection<string> SubscribedMessageTypes => _handlers.Keys;

    public bool TryGet(string typeName, out (Type MessageType, Type HandlerInterfaceType) entry)
        => _handlers.TryGetValue(typeName, out entry);
}

/// <summary>
/// RabbitMQ tüketici servisi:
/// - Topic exchange'e abone olduğu mesaj tiplerini (routing key) kendi kuyruğuna bağlar
/// - Inbox tablosu ile idempotency sağlar (aynı MessageId iki kez işlenmez)
/// - Hata durumunda mesajı TTL'li <c>.retry</c> kuyruğuna gönderir, <see cref="RabbitMqOptions.MaxRetries"/> aşılınca <c>.dlq</c> kuyruğuna taşır
/// </summary>
public sealed class RabbitMqConsumerWorker<TDbContext>(
    IServiceScopeFactory scopeFactory,
    RabbitMqConnection connection,
    MessageHandlerRegistry registry,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqConsumerWorker<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext, IMessagingDbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.QueueName) || registry.SubscribedMessageTypes.Count == 0)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            IChannel? channel = null;
            try
            {
                var conn = await connection.GetConnectionAsync(stoppingToken);
                channel = await conn.CreateChannelAsync(cancellationToken: stoppingToken);

                var mainQueue = o.QueueName;
                var retryQueue = $"{o.QueueName}.retry";
                var dlqQueue = $"{o.QueueName}.dlq";

                await channel.ExchangeDeclareAsync(o.Exchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);

                // Ana kuyruk
                await channel.QueueDeclareAsync(mainQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);

                // Retry kuyruğu: TTL dolunca mesajı varsayılan exchange ("") üzerinden ana kuyruğa geri yönlendirir
                var retryArgs = new Dictionary<string, object?>
                {
                    ["x-message-ttl"] = o.RetryDelayMs,
                    ["x-dead-letter-exchange"] = "",
                    ["x-dead-letter-routing-key"] = mainQueue
                };
                await channel.QueueDeclareAsync(retryQueue, durable: true, exclusive: false, autoDelete: false, arguments: retryArgs, cancellationToken: stoppingToken);

                // Dead-Letter Queue (DLQ)
                await channel.QueueDeclareAsync(dlqQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);

                foreach (var routingKey in registry.SubscribedMessageTypes)
                {
                    await channel.QueueBindAsync(mainQueue, o.Exchange, routingKey, cancellationToken: stoppingToken);
                }

                await channel.BasicQosAsync(0, o.PrefetchCount, global: false, cancellationToken: stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, ea) =>
                {
                    await ProcessDeliveryAsync(channel, ea, o, retryQueue, dlqQueue, stoppingToken);
                };

                await channel.BasicConsumeAsync(mainQueue, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
                logger.LogInformation("RabbitMQ tüketici hazır: Kuyruk={Queue}, Abonelikler=[{Types}]",
                    mainQueue, string.Join(", ", registry.SubscribedMessageTypes));

                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RabbitMQ tüketici döngüsünde hata; yeniden bağlanılacak");
                await Task.Delay(3000, stoppingToken);
            }
            finally
            {
                if (channel is not null)
                    await channel.DisposeAsync();
            }
        }
    }

    private async Task ProcessDeliveryAsync(
        IChannel channel,
        BasicDeliverEventArgs ea,
        RabbitMqOptions o,
        string retryQueue,
        string dlqQueue,
        CancellationToken ct)
    {
        var typeName = ea.BasicProperties.Type ?? ea.RoutingKey;
        var messageId = Guid.TryParse(ea.BasicProperties.MessageId, out var parsed) ? parsed : Guid.NewGuid();
        CorrelationContext.CorrelationId = ea.BasicProperties.CorrelationId ?? CorrelationContext.NewId();

        string? traceParent = null;
        if (ea.BasicProperties.Headers is not null &&
            ea.BasicProperties.Headers.TryGetValue(MessagingDiagnostics.TraceParentHeader, out var rawTp) &&
            rawTp is byte[] tpBytes)
        {
            traceParent = Encoding.UTF8.GetString(tpBytes);
        }

        ActivityContext.TryParse(traceParent, null, out var parentCtx);
        using var activity = MessagingDiagnostics.ActivitySource.StartActivity(
            $"{typeName} process", ActivityKind.Consumer, parentCtx);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", o.QueueName);
        activity?.SetTag("messaging.message.id", messageId);
        activity?.SetTag("correlation.id", CorrelationContext.CorrelationId);

        if (!registry.TryGet(typeName, out var entry))
        {
            logger.LogWarning("Kayıtsız mesaj tipi alındı ({Type}), ACK veriliyor", typeName);
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
            return;
        }

        try
        {
            var json = Encoding.UTF8.GetString(ea.Body.Span);
            var message = JsonSerializer.Deserialize(json, entry.MessageType, MessagingJson.Options)
                          ?? throw new InvalidOperationException($"{typeName} deserialize edilemedi.");

            await InboxProcessor.ExecuteIdempotentAsync<TDbContext>(
                scopeFactory,
                messageId,
                typeName,
                async sp =>
                {
                    var handler = sp.GetRequiredService(entry.HandlerInterfaceType);
                    var method = entry.HandlerInterfaceType.GetMethod(nameof(IMessageHandler<IIntegrationMessage>.HandleAsync))!;
                    var task = (Task)method.Invoke(handler, [message, ct])!;
                    await task;
                },
                logger,
                ct);

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var retryCount = GetRetryCount(ea.BasicProperties.Headers);

            var props = new BasicProperties
            {
                MessageId = messageId.ToString(),
                Type = typeName,
                CorrelationId = CorrelationContext.CorrelationId,
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Headers = new Dictionary<string, object?>
                {
                    [MessagingDiagnostics.RetryCountHeader] = retryCount + 1,
                    [MessagingDiagnostics.TraceParentHeader] = traceParent,
                    [MessagingDiagnostics.ErrorHeader] = ex.Message,
                    [MessagingDiagnostics.OriginalQueueHeader] = o.QueueName
                }
            };

            if (retryCount < o.MaxRetries)
            {
                logger.LogWarning(ex,
                    "Mesaj işlenemedi ({Type} {MessageId}), deneme {Retry}/{Max} -> {RetryQueue} kuyruğuna alınıyor",
                    typeName, messageId, retryCount + 1, o.MaxRetries, retryQueue);

                await channel.BasicPublishAsync("", retryQueue, mandatory: false, basicProperties: props, body: ea.Body, cancellationToken: ct);
            }
            else
            {
                logger.LogError(ex,
                    "Mesaj maksimum deneme sayısını ({Max}) aştı ({Type} {MessageId}) -> {DlqQueue} (DLQ) kuyruğuna taşındı",
                    o.MaxRetries, typeName, messageId, dlqQueue);

                await channel.BasicPublishAsync("", dlqQueue, mandatory: false, basicProperties: props, body: ea.Body, cancellationToken: ct);
            }

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
        }
    }

    private static int GetRetryCount(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue(MessagingDiagnostics.RetryCountHeader, out var val) || val is null)
            return 0;

        return val switch
        {
            int i => i,
            long l => (int)l,
            byte[] b when int.TryParse(Encoding.UTF8.GetString(b), out var p) => p,
            _ => 0
        };
    }
}

/// <summary>
/// Inbox idempotency yardımcısı (birim testlerinden de doğrudan çağrılabilir).
/// </summary>
public static class InboxProcessor
{
    public static async Task<bool> ExecuteIdempotentAsync<TDbContext>(
        IServiceScopeFactory scopeFactory,
        Guid messageId,
        string typeName,
        Func<IServiceProvider, Task> action,
        ILogger logger,
        CancellationToken ct = default)
        where TDbContext : DbContext, IMessagingDbContext
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

        if (await db.InboxMessages.AnyAsync(x => x.MessageId == messageId, ct))
        {
            logger.LogInformation("Mükerrer mesaj tespit edildi ve atlandı (Inbox): {Type} {MessageId}", typeName, messageId);
            return false;
        }

        db.InboxMessages.Add(new InboxMessage
        {
            MessageId = messageId,
            Type = typeName,
            ProcessedAt = DateTime.UtcNow
        });

        await action(scope.ServiceProvider);
        await db.SaveChangesAsync(ct);
        return true;
    }
}

