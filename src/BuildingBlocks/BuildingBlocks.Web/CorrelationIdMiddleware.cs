using System.Diagnostics;
using BuildingBlocks.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Web;

/// <summary>
/// Gelen istekte <c>X-Correlation-Id</c> varsa alır, yoksa üretir;
/// yanıt başlığına, log scope'una ve OpenTelemetry Activity tag'ine yazar.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(CorrelationContext.HeaderName, out var incoming)
                            && !string.IsNullOrWhiteSpace(incoming)
            ? incoming.ToString()
            : CorrelationContext.NewId();

        CorrelationContext.CorrelationId = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationContext.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        Activity.Current?.SetTag("correlation.id", correlationId);

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }
}

/// <summary>
/// Giden HttpClient çağrılarına <c>X-Correlation-Id</c> başlığını otomatik ekler.
/// </summary>
public sealed class CorrelationIdDelegatingHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(CorrelationContext.CorrelationId) &&
            !request.Headers.Contains(CorrelationContext.HeaderName))
        {
            request.Headers.TryAddWithoutValidation(CorrelationContext.HeaderName, CorrelationContext.CorrelationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}

