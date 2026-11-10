using Basket.Api;
using BuildingBlocks.Web;

var builder = WebApplication.CreateBuilder(args);
builder.AddStandardServiceDefaults("Basket.Api");

var redisConn = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false";
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisConn);
builder.Services.AddScoped<RedisBasketRepository>();

var app = builder.Build();
app.UseStandardServicePipeline();

var group = app.MapGroup("/api/basket");

group.MapGet("/{customerId}", async (string customerId, RedisBasketRepository repo, CancellationToken ct) =>
    Results.Ok(await repo.GetAsync(customerId, ct)));

group.MapPut("/{customerId}/items", async (string customerId, UpsertBasketItemRequest req, RedisBasketRepository repo, CancellationToken ct) =>
    Results.Ok(await repo.UpsertItemAsync(customerId, req, ct)));

group.MapDelete("/{customerId}/items/{productId:int}", async (string customerId, int productId, RedisBasketRepository repo, CancellationToken ct) =>
    Results.Ok(await repo.RemoveItemAsync(customerId, productId, ct)));

group.MapDelete("/{customerId}", async (string customerId, RedisBasketRepository repo, CancellationToken ct) =>
{
    await repo.ClearAsync(customerId, ct);
    return Results.NoContent();
});

app.Run();

