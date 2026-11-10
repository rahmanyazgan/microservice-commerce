using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.Web;

namespace Order.Api.Infrastructure;

public sealed record CatalogProductDto(int Id, string Name, decimal Price, int Stock, bool IsActive);

public interface ICatalogClient
{
    Task<CatalogProductDto> GetProductAsync(int productId, CancellationToken ct);
}

/// <summary>
/// Sipariş oluşturulurken ürün adını ve güncel fiyatını Catalog servisinden doğrular.
/// Bu HttpClient, Program.cs içinde Polly (<c>AddStandardResilienceHandler</c>) ile
/// üstel geri çekilmeli retry, circuit breaker ve timeout politikalarına sahiptir.
/// </summary>
public sealed class CatalogHttpClient(HttpClient httpClient) : ICatalogClient
{
    public async Task<CatalogProductDto> GetProductAsync(int productId, CancellationToken ct)
    {
        var response = await httpClient.GetAsync($"/api/products/{productId}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new DomainException($"#{productId} numaralı ürün katalogda bulunamadı.", 404);

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CatalogProductDto>(cancellationToken: ct))!;
    }
}

