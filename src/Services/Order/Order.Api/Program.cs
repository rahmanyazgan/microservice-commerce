using BuildingBlocks.Contracts;
using BuildingBlocks.Messaging;
using BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using Order.Api.Application;
using Order.Api.Domain;
using Order.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddStandardServiceDefaults("Order.Api");

var connStr = builder.Configuration.GetConnectionString("OrderDb")
              ?? "Server=(localdb)\\MSSQLLocalDB;Database=ETicaret_OrderDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

builder.Services.AddDbContext<OrderDbContext>(o => o.UseSqlServer(connStr));
builder.Services.AddSingleton<OrderReportQueryService>();

// Polly (StandardResilienceHandler): Retry (exponential backoff + jitter), Circuit Breaker ve Timeout
var catalogBaseUrl = builder.Configuration["Services:CatalogUrl"] ?? "http://localhost:5101";
builder.Services.AddHttpClient<ICatalogClient, CatalogHttpClient>(client =>
    {
        client.BaseAddress = new Uri(catalogBaseUrl);
    })
    .AddHttpMessageHandler<CorrelationIdDelegatingHandler>()
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.Delay = TimeSpan.FromMilliseconds(250);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(12);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    });

builder.Services.AddRabbitMqMessaging<OrderDbContext>(builder.Configuration, queueName: "order")
    .AddHandler<StockReserved, SagaEventHandlers>()
    .AddHandler<StockReservationFailed, SagaEventHandlers>()
    .AddHandler<PaymentSucceeded, SagaEventHandlers>()
    .AddHandler<PaymentFailed, SagaEventHandlers>();

builder.Services.AddHostedService<SagaTimeoutWorker>();

var app = builder.Build();
app.UseStandardServicePipeline();

await InitializeDatabaseAsync(app.Services);

var group = app.MapGroup("/api/orders");

group.MapPost("/", async (
    CreateOrderRequest req,
    ICatalogClient catalog,
    OrderDbContext db,
    CancellationToken ct) =>
{
    if (req.Items is null || req.Items.Count == 0)
        throw new DomainException("Sipariş kalemleri boş olamaz.");

    var resolvedItems = new List<(int ProductId, string ProductName, decimal UnitPrice, int Quantity)>();
    foreach (var line in req.Items)
    {
        var product = await catalog.GetProductAsync(line.ProductId, ct);
        resolvedItems.Add((product.Id, product.Name, product.Price, line.Quantity));
    }

    var (order, messages) = OrderEntity.Create(req.CustomerId, resolvedItems);
    db.Orders.Add(order);
    foreach (var msg in messages)
    {
        if (msg is ReserveStock reserve)
            db.AddOutboxMessage(reserve);
    }

    await db.SaveChangesAsync(ct);
    return Results.Accepted($"/api/orders/{order.Id}", MapOrder(order));
});

group.MapGet("/", async (string? customerId, OrderDbContext db, CancellationToken ct) =>
{
    var cid = string.IsNullOrWhiteSpace(customerId) ? "demo-musteri" : customerId.Trim();
    var orders = await db.Orders.AsNoTracking()
        .Include(o => o.Items)
        .Include(o => o.History)
        .Where(o => o.CustomerId == cid)
        .OrderByDescending(o => o.CreatedAt)
        .Take(50)
        .ToListAsync(ct);

    return Results.Ok(orders.Select(MapOrder));
});

group.MapGet("/{id:guid}", async (Guid id, OrderDbContext db, CancellationToken ct) =>
{
    var order = await db.Orders.AsNoTracking()
        .Include(o => o.Items)
        .Include(o => o.History)
        .FirstOrDefaultAsync(o => o.Id == id, ct)
        ?? throw new DomainException("Sipariş bulunamadı.", 404);

    return Results.Ok(MapOrder(order));
});

group.MapGet("/reports/daily", async (int? days, OrderReportQueryService reportService, CancellationToken ct) =>
    Results.Ok(await reportService.GetDailySalesAsync(days ?? 30, ct)));

app.Run();

static object MapOrder(OrderEntity o) => new
{
    o.Id,
    o.CustomerId,
    Status = o.Status.ToString(),
    o.StatusText,
    o.TotalAmount,
    o.CancelReason,
    o.PaymentId,
    o.CreatedAt,
    Items = o.Items.Select(i => new
    {
        i.ProductId,
        i.ProductName,
        i.UnitPrice,
        i.Quantity
    }),
    History = o.History
        .OrderBy(h => h.OccurredAt)
        .ThenBy(h => h.Id)
        .Select(h => new
        {
            Status = h.Status.ToString(),
            h.Note,
            h.OccurredAt
        })
};

static async Task InitializeDatabaseAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    await db.Database.EnsureCreatedAsync();
    await OrderReportQueryService.EnsureStoredProcedureAndSeedAsync(db);

    if (await db.Orders.AnyAsync())
        return;

    // Rapor ekranının ilk açılışta dolu görünmesi için geçmiş günlere ait örnek onaylı siparişler
    var sampleDays = new[] { -6, -5, -4, -3, -2, -1 };
    var rnd = new Random(42);
    foreach (var offset in sampleDays)
    {
        var count = rnd.Next(2, 5);
        for (var i = 0; i < count; i++)
        {
            var created = DateTime.UtcNow.Date.AddDays(offset).AddHours(10 + i);
            var price = rnd.Next(450, 3500);
            var orderId = Guid.NewGuid();
            db.Orders.Add(new OrderEntity
            {
                Id = orderId,
                CustomerId = "arşiv-musteri",
                Status = OrderStatus.Confirmed,
                TotalAmount = price,
                CreatedAt = created,
                UpdatedAt = created.AddMinutes(1),
                Items =
                [
                    new OrderItem
                    {
                        OrderId = orderId,
                        ProductId = 1,
                        ProductName = "Örnek Geçmiş Sipariş Kalemi",
                        UnitPrice = price,
                        Quantity = 1
                    }
                ],
                History =
                [
                    new OrderHistoryEntry
                    {
                        OrderId = orderId,
                        Status = OrderStatus.Confirmed,
                        Note = "Geçmiş dönem örnek satış kaydı.",
                        OccurredAt = created
                    }
                ]
            });
        }
    }

    await db.SaveChangesAsync();
}

public sealed record CreateOrderItemRequest(int ProductId, int Quantity);
public sealed record CreateOrderRequest(string CustomerId, List<CreateOrderItemRequest> Items);

