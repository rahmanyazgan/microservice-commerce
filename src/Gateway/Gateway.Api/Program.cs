using System.Diagnostics;
using System.Threading.RateLimiting;
using BuildingBlocks.Contracts;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.RateLimiting;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);
builder.AddStandardServiceDefaults("Gateway.Api");

builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddFixedWindowLimiter("genel", opt =>
    {
        opt.Window = TimeSpan.FromSeconds(10);
        opt.PermitLimit = 200;
        opt.QueueLimit = 20;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

builder.Services.AddHttpClient("health-probe", c => c.Timeout = TimeSpan.FromSeconds(2));

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(ctx =>
    {
        ctx.AddRequestTransform(reqCtx =>
        {
            if (!string.IsNullOrEmpty(CorrelationContext.CorrelationId))
            {
                reqCtx.ProxyRequest.Headers.Remove(CorrelationContext.HeaderName);
                reqCtx.ProxyRequest.Headers.TryAddWithoutValidation(CorrelationContext.HeaderName, CorrelationContext.CorrelationId);
            }
            return ValueTask.CompletedTask;
        });
    });

var app = builder.Build();
app.UseCors();
app.UseStandardServicePipeline();
app.UseRateLimiter();

var targets = new (string Key, string Name, string BaseUrl, string HealthUrl)[]
{
    ("catalog", "Katalog", "http://localhost:5101", "http://localhost:5101/health"),
    ("basket", "Sepet", "http://localhost:5102", "http://localhost:5102/health"),
    ("order", "Sipariş", "http://localhost:5103", "http://localhost:5103/health"),
    ("payment", "Ödeme", "http://localhost:5104", "http://localhost:5104/health"),
    ("notification", "Bildirim", "http://localhost:5105", "http://localhost:5105/health"),
    ("identity", "Kimlik", "http://localhost:5106", "http://localhost:5106/health")
};

app.MapGet("/api/health", async (IHttpClientFactory httpFactory, CancellationToken ct) =>
{
    var client = httpFactory.CreateClient("health-probe");
    var tasks = targets.Select(async t =>
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var resp = await client.GetAsync(t.HealthUrl, ct);
            sw.Stop();
            return new
            {
                name = t.Name,
                status = resp.IsSuccessStatusCode ? "Healthy" : "Unhealthy",
                durationMs = sw.ElapsedMilliseconds
            };
        }
        catch
        {
            sw.Stop();
            return new
            {
                name = t.Name,
                status = "Unhealthy",
                durationMs = sw.ElapsedMilliseconds
            };
        }
    });

    var results = await Task.WhenAll(tasks);
    var allHealthy = results.All(r => r.status == "Healthy");
    return Results.Ok(new
    {
        status = allHealthy ? "Healthy" : "Degraded",
        services = results
    });
});

app.MapGet("/api/openapi", () => Results.Ok(targets.Select(t => new
{
    key = t.Key,
    name = $"{t.Name} Servisi",
    url = $"/api/openapi/{t.Key}"
})));

app.MapGet("/api/openapi/{service}", async (string service, IHttpClientFactory httpFactory, CancellationToken ct) =>
{
    var match = targets.FirstOrDefault(t => string.Equals(t.Key, service, StringComparison.OrdinalIgnoreCase));
    if (match.Key is null)
        return Results.NotFound();

    var client = httpFactory.CreateClient("health-probe");
    var json = await client.GetStringAsync($"{match.BaseUrl}/openapi/v1.json", ct);
    return Results.Content(json, "application/json");
});

app.MapGet("/swagger", () => Results.Content("""
    <!doctype html>
    <html lang="tr">
    <head>
      <meta charset="utf-8" />
      <title>Mini Mağaza — Birleşik Mikroservis API Dokümantasyonu (Swagger UI)</title>
      <link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist@5/swagger-ui.css" />
      <style>body { margin: 0; background: #f8fafc; }</style>
    </head>
    <body>
      <div id="swagger-ui"></div>
      <script src="https://unpkg.com/swagger-ui-dist@5/swagger-ui-bundle.js"></script>
      <script src="https://unpkg.com/swagger-ui-dist@5/swagger-ui-standalone-preset.js"></script>
      <script>
        window.onload = () => {
          window.ui = SwaggerUIBundle({
            urls: [
              { url: '/api/openapi/catalog', name: '1. Katalog Servisi (Catalog.Api)' },
              { url: '/api/openapi/basket', name: '2. Sepet Servisi (Basket.Api)' },
              { url: '/api/openapi/order', name: '3. Sipariş & Saga Servisi (Order.Api)' },
              { url: '/api/openapi/payment', name: '4. Ödeme & ISO 8583 Servisi (Payment.Api)' },
              { url: '/api/openapi/notification', name: '5. Bildirim Servisi (Notification.Worker)' },
              { url: '/api/openapi/identity', name: '6. Kimlik & Google Auth Servisi (Identity.Api)' }
            ],
            dom_id: '#swagger-ui',
            deepLinking: true,
            presets: [SwaggerUIBundle.presets.apis, SwaggerUIStandalonePreset],
            layout: 'StandaloneLayout'
          });
        };
      </script>
    </body>
    </html>
    """, "text/html"));

app.MapReverseProxy().RequireRateLimiting("genel");

app.Run();

