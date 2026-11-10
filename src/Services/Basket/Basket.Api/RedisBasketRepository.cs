using System.Collections.Concurrent;
using System.Text.Json;
using BuildingBlocks.Web;
using Microsoft.Extensions.Caching.Distributed;

namespace Basket.Api;

public sealed record BasketItemDto(int ProductId, string Name, decimal Price, int Quantity)
{
    public decimal LineTotal => decimal.Round(Price * Quantity, 2);
}

public sealed record CustomerBasket(string CustomerId, List<BasketItemDto> Items)
{
    public decimal Total => decimal.Round(Items.Sum(i => i.LineTotal), 2);
}

public sealed record UpsertBasketItemRequest(int ProductId, string Name, decimal Price, int Quantity);

/// <summary>
/// Sepet verilerini Redis üzerinde <c>basket:{customerId}</c> anahtarıyla ve 24 saatlik kayan TTL ile tutar.
/// </summary>
public sealed class RedisBasketRepository(IDistributedCache cache, ILogger<RedisBasketRepository> logger)
{
    private static readonly ConcurrentDictionary<string, CustomerBasket> FallbackStore = new();
    private static readonly DistributedCacheEntryOptions Options = new()
    {
        SlidingExpiration = TimeSpan.FromHours(24)
    };

    private static string Key(string customerId) => $"basket:{customerId.Trim().ToLowerInvariant()}";

    public async Task<CustomerBasket> GetAsync(string customerId, CancellationToken ct)
    {
        try
        {
            var json = await cache.GetStringAsync(Key(customerId), ct);
            if (!string.IsNullOrEmpty(json))
                return JsonSerializer.Deserialize<CustomerBasket>(json)!;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis sepet okuma hatası, bellek içi yedeğe düşüldü");
            if (FallbackStore.TryGetValue(Key(customerId), out var fb))
                return fb;
        }

        return new CustomerBasket(customerId, []);
    }

    public async Task<CustomerBasket> UpsertItemAsync(string customerId, UpsertBasketItemRequest req, CancellationToken ct)
    {
        if (req.ProductId <= 0)
            throw new DomainException("Geçersiz ürün numarası.");
        if (req.Price < 0)
            throw new DomainException("Ürün fiyatı negatif olamaz.");

        var basket = await GetAsync(customerId, ct);
        var existingIndex = basket.Items.FindIndex(i => i.ProductId == req.ProductId);

        if (req.Quantity <= 0)
        {
            if (existingIndex >= 0)
                basket.Items.RemoveAt(existingIndex);
        }
        else
        {
            var item = new BasketItemDto(req.ProductId, req.Name, decimal.Round(req.Price, 2), req.Quantity);
            if (existingIndex >= 0)
                basket.Items[existingIndex] = item;
            else
                basket.Items.Add(item);
        }

        await SaveAsync(basket, ct);
        return basket;
    }

    public async Task<CustomerBasket> RemoveItemAsync(string customerId, int productId, CancellationToken ct)
    {
        var basket = await GetAsync(customerId, ct);
        basket.Items.RemoveAll(i => i.ProductId == productId);
        await SaveAsync(basket, ct);
        return basket;
    }

    public async Task ClearAsync(string customerId, CancellationToken ct)
    {
        FallbackStore.TryRemove(Key(customerId), out _);
        try
        {
            await cache.RemoveAsync(Key(customerId), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis sepet silme hatası");
        }
    }

    private async Task SaveAsync(CustomerBasket basket, CancellationToken ct)
    {
        FallbackStore[Key(basket.CustomerId)] = basket;
        try
        {
            var json = JsonSerializer.Serialize(basket);
            await cache.SetStringAsync(Key(basket.CustomerId), json, Options, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis sepet yazma hatası, bellek içinde tutuldu");
        }
    }
}

