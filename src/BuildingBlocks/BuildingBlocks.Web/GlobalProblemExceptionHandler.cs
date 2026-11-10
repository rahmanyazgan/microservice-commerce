using System.Diagnostics;
using BuildingBlocks.Contracts;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Web;

/// <summary>
/// RFC 7807 ProblemDetails standardında merkezi hata yöneticisi.
/// Tüm yanıtlar Türkçe başlık/detay, traceId ve correlationId içerir.
/// </summary>
public sealed class GlobalProblemExceptionHandler(ILogger<GlobalProblemExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            DomainException d => (d.StatusCode, d.StatusCode switch
            {
                404 => "Kayıt bulunamadı",
                409 => "İşlem çakışması",
                _ => "Geçersiz istek"
            }),
            SimulatedFaultException => (StatusCodes.Status503ServiceUnavailable, "Simüle edilen geçici servis hatası"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Kayıt bulunamadı"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Geçersiz parametre"),
            _ => (StatusCodes.Status500InternalServerError, "Sunucu hatası")
        };

        if (status >= 500)
            logger.LogError(exception, "İşlenmeyen hata (CorrelationId={CorrelationId})", CorrelationContext.CorrelationId);
        else
            logger.LogWarning("İstek reddedildi ({Status}): {Message}", status, exception.Message);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = exception.Message,
            Instance = httpContext.Request.Path,
            Type = $"https://httpstatuses.io/{status}"
        };

        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        problem.Extensions["correlationId"] = CorrelationContext.CorrelationId;

        if (exception is DomainException { Errors: not null } domEx)
            problem.Extensions["errors"] = domEx.Errors;

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}

