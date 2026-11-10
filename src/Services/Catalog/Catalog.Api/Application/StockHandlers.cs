using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using Catalog.Api.Domain;
using Catalog.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Catalog.Api.Application;

/// <summary>
/// Saga Adım 1: Sipariş kalemleri için stok kontrolü yapar.
/// Tüm kalemlerde yeterli stok varsa düşer ve <see cref="StockReserved"/> yayınlar;
/// aksi halde hiçbir stoğa dokunmadan <see cref="StockReservationFailed"/> yayınlar (Outbox ile atomik).
/// </summary>
public sealed class ReserveStockHandler(
    CatalogDbContext db,
    IProductCatalogReader reader,
    ILogger<ReserveStockHandler> logger) : IMessageHandler<ReserveStock>
{
    public async Task HandleAsync(ReserveStock message, CancellationToken ct)
    {
        var ids = message.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products
            .Where(p => ids.Contains(p.Id) && p.IsActive)
            .ToDictionaryAsync(p => p.Id, ct);

        foreach (var line in message.Items)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                var reason = $"Ürün bulunamadı veya satış dışı (#{line.ProductId}).";
                db.AddOutboxMessage(new StockReservationFailed(message.OrderId, reason));
                logger.LogWarning("Stok ayrılamadı (Sipariş={OrderId}): {Reason}", message.OrderId, reason);
                return;
            }

            if (product.Stock < line.Quantity)
            {
                var reason = $"'{product.Name}' için yetersiz stok (İstenen: {line.Quantity}, Mevcut: {product.Stock}).";
                db.AddOutboxMessage(new StockReservationFailed(message.OrderId, reason));
                logger.LogWarning("Stok ayrılamadı (Sipariş={OrderId}): {Reason}", message.OrderId, reason);
                return;
            }
        }

        foreach (var line in message.Items)
        {
            var product = products[line.ProductId];
            product.Stock -= line.Quantity;

            db.StockReservations.Add(new StockReservation
            {
                OrderId = message.OrderId,
                ProductId = line.ProductId,
                Quantity = line.Quantity
            });
        }

        db.AddOutboxMessage(new StockReserved(message.OrderId));
        await reader.InvalidateCacheAsync(ct: ct);
        logger.LogInformation("Stok ayrıldı: Sipariş={OrderId}, Kalem={Count}", message.OrderId, message.Items.Count);
    }
}

/// <summary>
/// Saga Telafi (Compensation) Adımı: Ödeme reddedildiğinde veya zaman aşımına uğradığında
/// ayrılmış stokları geri iade eder.
/// </summary>
public sealed class ReleaseStockHandler(
    CatalogDbContext db,
    IProductCatalogReader reader,
    ILogger<ReleaseStockHandler> logger) : IMessageHandler<ReleaseStock>
{
    public async Task HandleAsync(ReleaseStock message, CancellationToken ct)
    {
        var reservations = await db.StockReservations
            .Where(r => r.OrderId == message.OrderId && !r.Released)
            .ToListAsync(ct);

        if (reservations.Count == 0)
            return;

        var productIds = reservations.Select(r => r.ProductId).Distinct().ToList();
        var products = await db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        foreach (var res in reservations)
        {
            if (products.TryGetValue(res.ProductId, out var product))
                product.Stock += res.Quantity;

            res.Released = true;
        }

        await reader.InvalidateCacheAsync(ct: ct);
        logger.LogInformation("Ayrılan stok geri bırakıldı (Telafi): Sipariş={OrderId}, Neden={Reason}",
            message.OrderId, message.Reason);
    }
}

