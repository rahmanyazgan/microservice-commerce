using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Order.Api.Domain;
using Order.Api.Infrastructure;

namespace Order.Api.Application;

public sealed class SagaEventHandlers(OrderDbContext db, ILogger<SagaEventHandlers> logger)
    : IMessageHandler<StockReserved>,
      IMessageHandler<StockReservationFailed>,
      IMessageHandler<PaymentSucceeded>,
      IMessageHandler<PaymentFailed>
{
    public async Task HandleAsync(StockReserved message, CancellationToken ct)
    {
        var order = await LoadOrderAsync(message.OrderId, ct);
        if (order is null) return;

        foreach (var outgoing in order.OnStockReserved())
            EnqueueDynamic(outgoing);

        logger.LogInformation("Saga [{OrderId}] -> StockReserved", order.Id);
    }

    public async Task HandleAsync(StockReservationFailed message, CancellationToken ct)
    {
        var order = await LoadOrderAsync(message.OrderId, ct);
        if (order is null) return;

        foreach (var outgoing in order.OnStockReservationFailed(message.Reason))
            EnqueueDynamic(outgoing);

        logger.LogWarning("Saga [{OrderId}] -> Cancelled (Stok hatası: {Reason})", order.Id, message.Reason);
    }

    public async Task HandleAsync(PaymentSucceeded message, CancellationToken ct)
    {
        var order = await LoadOrderAsync(message.OrderId, ct);
        if (order is null) return;

        foreach (var outgoing in order.OnPaymentSucceeded(message.PaymentId, message.AuthCode))
            EnqueueDynamic(outgoing);

        logger.LogInformation("Saga [{OrderId}] -> PaymentSucceeded (Durum={Status})", order.Id, order.Status);
    }

    public async Task HandleAsync(PaymentFailed message, CancellationToken ct)
    {
        var order = await LoadOrderAsync(message.OrderId, ct);
        if (order is null) return;

        foreach (var outgoing in order.OnPaymentFailed(message.PaymentId, message.Reason, message.ResponseCode))
            EnqueueDynamic(outgoing);

        logger.LogWarning("Saga [{OrderId}] -> Cancelled & ReleaseStock (Ödeme hatası: {Reason})", order.Id, message.Reason);
    }

    private Task<OrderEntity?> LoadOrderAsync(Guid id, CancellationToken ct)
        => db.Orders
            .Include(o => o.Items)
            .Include(o => o.History)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    private void EnqueueDynamic(IIntegrationMessage msg)
    {
        switch (msg)
        {
            case ReserveStock m: db.AddOutboxMessage(m); break;
            case ReleaseStock m: db.AddOutboxMessage(m); break;
            case RequestPayment m: db.AddOutboxMessage(m); break;
            case RefundPayment m: db.AddOutboxMessage(m); break;
            case OrderConfirmed m: db.AddOutboxMessage(m); break;
            case OrderCancelled m: db.AddOutboxMessage(m); break;
        }
    }
}

/// <summary>
/// Saga Zaman Aşımı İşçisi: Belirli süre (varsayılan 10 dk) içinde ödemesi tamamlanmayan
/// <c>Pending</c> veya <c>StockReserved</c> siparişleri iptal edip ayrılan stoğu geri bıraktırır.
/// </summary>
public sealed class SagaTimeoutWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<SagaTimeoutWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timeoutMinutes = configuration.GetValue("Saga:PaymentTimeoutMinutes", 10);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
                var threshold = DateTime.UtcNow.AddMinutes(-timeoutMinutes);

                var expiredOrders = await db.Orders
                    .Include(o => o.History)
                    .Where(o => (o.Status == OrderStatus.Pending || o.Status == OrderStatus.StockReserved)
                                && o.CreatedAt < threshold)
                    .Take(20)
                    .ToListAsync(stoppingToken);

                foreach (var order in expiredOrders)
                {
                    foreach (var msg in order.OnTimedOut())
                    {
                        switch (msg)
                        {
                            case ReleaseStock m: db.AddOutboxMessage(m); break;
                            case OrderCancelled m: db.AddOutboxMessage(m); break;
                        }
                    }

                    logger.LogWarning("Sipariş zaman aşımına uğradı ve telafi başlatıldı: {OrderId}", order.Id);
                }

                if (expiredOrders.Count > 0)
                    await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SagaTimeoutWorker döngüsünde hata");
            }
        }
    }
}

