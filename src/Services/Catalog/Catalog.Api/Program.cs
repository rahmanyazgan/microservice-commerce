using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using Catalog.Api.Application;
using Catalog.Api.Domain;
using Catalog.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddStandardServiceDefaults("Catalog.Api");

var connStr = builder.Configuration.GetConnectionString("CatalogDb")
              ?? "Server=(localdb)\\MSSQLLocalDB;Database=ETicaret_CatalogDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

builder.Services.AddDbContext<CatalogDbContext>(o => o.UseSqlServer(connStr));

var redisConn = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false";
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisConn);

builder.Services.AddScoped<EfProductCatalogReader>();
builder.Services.AddScoped<IProductCatalogReader, CachedProductCatalogReader>();

builder.Services.AddRabbitMqMessaging<CatalogDbContext>(builder.Configuration, queueName: "catalog")
    .AddHandler< BuildingBlocks.Contracts.ReserveStock, ReserveStockHandler>()
    .AddHandler<BuildingBlocks.Contracts.ReleaseStock, ReleaseStockHandler>();

var app = builder.Build();
app.UseStandardServicePipeline();

await SeedAsync(app.Services);

var group = app.MapGroup("/api/products");

group.MapGet("/", async (
    string? search,
    string? category,
    int? page,
    int? pageSize,
    IProductCatalogReader reader,
    ChaosState chaos,
    CancellationToken ct) =>
{
    await chaos.ApplyAsync("Katalog Servisi", ct);
    var p = Math.Max(1, page ?? 1);
    var ps = Math.Clamp(pageSize ?? 20, 1, 100);
    return Results.Ok(await reader.ListAsync(search, category, p, ps, ct));
});

group.MapGet("/categories", async (IProductCatalogReader reader, CancellationToken ct) =>
    Results.Ok(await reader.GetCategoriesAsync(ct)));

group.MapGet("/chaos", (ChaosState chaos) => Results.Ok(chaos.Get()));
group.MapPut("/chaos", (ChaosSettings settings, ChaosState chaos) => Results.Ok(chaos.Update(settings)))
    .RequireAuthorization(JwtAuthConstants.AdminPolicy);

group.MapGet("/{id:int}", async (int id, IProductCatalogReader reader, ChaosState chaos, CancellationToken ct) =>
{
    await chaos.ApplyAsync("Katalog Servisi", ct);
    var product = await reader.GetByIdAsync(id, ct)
                  ?? throw new DomainException($"#{id} numaralı ürün bulunamadı.", 404);
    return Results.Ok(product);
});

group.MapPost("/", async (ProductUpsertDto dto, CatalogDbContext db, IProductCatalogReader reader, CancellationToken ct) =>
{
    var product = new Product();
    product.UpdateDetails(dto.Name, dto.Description, dto.Category, dto.Price, dto.Stock, dto.ImageUrl);
    db.Products.Add(product);
    await db.SaveChangesAsync(ct);
    await reader.InvalidateCacheAsync(product.Id, ct);
    return Results.Created($"/api/products/{product.Id}", product);
}).RequireAuthorization(JwtAuthConstants.AdminPolicy);

group.MapPut("/{id:int}", async (int id, ProductUpsertDto dto, CatalogDbContext db, IProductCatalogReader reader, CancellationToken ct) =>
{
    var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
                  ?? throw new DomainException($"#{id} numaralı ürün bulunamadı.", 404);
    product.UpdateDetails(dto.Name, dto.Description, dto.Category, dto.Price, dto.Stock, dto.ImageUrl);
    product.IsActive = true;
    await db.SaveChangesAsync(ct);
    await reader.InvalidateCacheAsync(product.Id, ct);
    return Results.Ok(product);
}).RequireAuthorization(JwtAuthConstants.AdminPolicy);

group.MapDelete("/{id:int}", async (int id, CatalogDbContext db, IProductCatalogReader reader, CancellationToken ct) =>
{
    var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
                  ?? throw new DomainException($"#{id} numaralı ürün bulunamadı.", 404);
    product.IsActive = false;
    await db.SaveChangesAsync(ct);
    await reader.InvalidateCacheAsync(product.Id, ct);
    return Results.NoContent();
}).RequireAuthorization(JwtAuthConstants.AdminPolicy);

app.Run();

static async Task SeedAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await db.Database.EnsureCreatedAsync();

    if (await db.Products.AnyAsync())
        return;

    var initial = new (string Name, string Desc, string Cat, decimal Price, int Stock)[]
    {
        ("Mekanik Klavye (Türkçe Q)", "Hot-swap switch, beyaz arka aydınlatma, alüminyum kasa", "Elektronik", 2450m, 18),
        ("Kablosuz Ergonomik Mouse", "Sessiz tıklama, çift mod (Bluetooth + 2.4GHz), USB-C şarj", "Elektronik", 1150m, 30),
        ("27\" 2K IPS Monitör", "75Hz, %99 sRGB, ince çerçeve, yükseklik ayarlı stand", "Elektronik", 7890m, 8),
        ("Gürültü Önleyici Kulaklık", "Aktif gürültü engelleme (ANC), 35 saat pil ömrü", "Elektronik", 4250m, 12),
        ("USB-C Çoklayıcı Hub (7-in-1)", "4K HDMI, 100W PD, SD kart okuyucu, 3x USB 3.0", "Aksesuar", 890m, 45),
        ("Alüminyum Laptop Standı", "Katlanabilir, kaymaz silikon pedli, 11-17 inç uyumlu", "Aksesuar", 640m, 25),
        ("Deri Masa Matı (80x40 cm)", "Su geçirmez çift taraflı yüzey, dikişli kenar", "Ofis", 480m, 40),
        ("Masaüstü LED Çalışma Lambası", "Dokunmatik kısılabilir, 3 renk sıcaklığı, göz koruma modu", "Ofis", 920m, 15),
        ("Termos Kupa (500 ml)", "Çift duvarlı paslanmaz çelik, 8 saat sıcak tutma", "Yaşam", 560m, 50),
        ("Dağıtık Sistemler Kitabı", "Mikroservisler, Saga, Outbox ve veri tutarlılığı desenleri", "Kitap", 420m, 20),
        ("Sınırlı Üretim Koleksiyon Kalemi", "Stok yetersizliği senaryosunu test etmek için 0 stoklu ürün", "Ofis", 1500m, 0)
    };

    foreach (var item in initial)
    {
        var p = new Product();
        p.UpdateDetails(item.Name, item.Desc, item.Cat, item.Price, item.Stock, null);
        db.Products.Add(p);
    }

    await db.SaveChangesAsync();
}

public sealed record ProductUpsertDto(
    string Name,
    string Description,
    string Category,
    decimal Price,
    int Stock,
    string? ImageUrl);

