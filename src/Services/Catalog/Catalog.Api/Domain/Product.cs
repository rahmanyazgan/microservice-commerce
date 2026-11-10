using BuildingBlocks.Web;

namespace Catalog.Api.Domain;

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public void UpdateDetails(string name, string description, string category, decimal price, int stock, string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Ürün adı boş olamaz.");
        if (price <= 0)
            throw new DomainException("Ürün fiyatı 0'dan büyük olmalıdır.");
        if (stock < 0)
            throw new DomainException("Stok miktarı negatif olamaz.");

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
        Category = string.IsNullOrWhiteSpace(category) ? "Genel" : category.Trim();
        Price = decimal.Round(price, 2);
        Stock = stock;
        ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim();
    }
}

/// <summary>
/// Hangi sipariş için hangi üründen kaç adet stok ayrıldığını tutar.
/// Ödeme başarısız olursa (Saga telafisi / ReleaseStock) bu kayıtlar üzerinden stok geri yüklenir.
/// </summary>
public sealed class StockReservation
{
    public int Id { get; set; }
    public Guid OrderId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public bool Released { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

