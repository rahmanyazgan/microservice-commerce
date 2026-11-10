using System.Text.Json;
using Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Catalog.Api.Infrastructure;

public sealed record PagedProducts(IReadOnlyList<Product> Items, int Total, int Page, int PageSize);

public interface IProductCatalogReader
{
    Task<PagedProducts> ListAsync(string? search, string? category, int page, int pageSize, CancellationToken ct);
    Task<Product?> GetByIdAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken ct);
    Task InvalidateCacheAsync(int? productId = null, CancellationToken ct = default);
}

/// <summary> Veritabanından doğrudan okuma yapan iç (inner) somut sınıf. </summary>
public sealed class EfProductCatalogReader(CatalogDbContext db) : IProductCatalogReader
{
    public async Task<PagedProducts> ListAsync(string? search, string? category, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Products.AsNoTracking().Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => p.Category == category);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p => p.Name.Contains(term) || p.Description.Contains(term));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(p => p.Category)
            .ThenBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedProducts(items, total, page, pageSize);
    }

    public Task<Product?> GetByIdAsync(int id, CancellationToken ct)
        => db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.IsActive, ct);

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken ct)
        => await db.Products.AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => p.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);

    public Task InvalidateCacheAsync(int? productId = null, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>
/// Decorator tasarım kalıbı: <see cref="EfProductCatalogReader"/> etrafına Redis Cache-Aside katmanı sarar.
/// Stok değiştiğinde veya ürün güncellendiğinde versiyon anahtarı artırılarak önbellek anında geçersiz kılınır.
/// </summary>
public sealed class CachedProductCatalogReader(
    EfProductCatalogReader inner,
    IDistributedCache cache,
    ILogger<CachedProductCatalogReader> logger) : IProductCatalogReader
{
    private const string VersionKey = "catalog:version";
    private static readonly DistributedCacheEntryOptions Ttl = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
    };

    public async Task<PagedProducts> ListAsync(string? search, string? category, int page, int pageSize, CancellationToken ct)
    {
        try
        {
            var ver = await GetVersionAsync(ct);
            var key = $"catalog:v{ver}:list:{category ?? "_"}:{search ?? "_"}:{page}:{pageSize}";
            var cached = await cache.GetStringAsync(key, ct);
            if (cached is not null)
                return JsonSerializer.Deserialize<PagedProducts>(cached)!;

            var fresh = await inner.ListAsync(search, category, page, pageSize, ct);
            await cache.SetStringAsync(key, JsonSerializer.Serialize(fresh), Ttl, ct);
            return fresh;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis önbellek erişim hatası; doğrudan SQL'den okunuyor");
            return await inner.ListAsync(search, category, page, pageSize, ct);
        }
    }

    public async Task<Product?> GetByIdAsync(int id, CancellationToken ct)
    {
        try
        {
            var ver = await GetVersionAsync(ct);
            var key = $"catalog:v{ver}:product:{id}";
            var cached = await cache.GetStringAsync(key, ct);
            if (cached is not null)
                return JsonSerializer.Deserialize<Product>(cached);

            var fresh = await inner.GetByIdAsync(id, ct);
            if (fresh is not null)
                await cache.SetStringAsync(key, JsonSerializer.Serialize(fresh), Ttl, ct);
            return fresh;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis önbellek erişim hatası; doğrudan SQL'den okunuyor");
            return await inner.GetByIdAsync(id, ct);
        }
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken ct)
    {
        try
        {
            var ver = await GetVersionAsync(ct);
            var key = $"catalog:v{ver}:categories";
            var cached = await cache.GetStringAsync(key, ct);
            if (cached is not null)
                return JsonSerializer.Deserialize<List<string>>(cached)!;

            var fresh = await inner.GetCategoriesAsync(ct);
            await cache.SetStringAsync(key, JsonSerializer.Serialize(fresh), Ttl, ct);
            return fresh;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis önbellek erişim hatası; doğrudan SQL'den okunuyor");
            return await inner.GetCategoriesAsync(ct);
        }
    }

    public async Task InvalidateCacheAsync(int? productId = null, CancellationToken ct = default)
    {
        try
        {
            var nextVersion = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            await cache.SetStringAsync(VersionKey, nextVersion, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis önbellek temizlenemedi");
        }
    }

    private async Task<string> GetVersionAsync(CancellationToken ct)
        => await cache.GetStringAsync(VersionKey, ct) ?? "1";
}

