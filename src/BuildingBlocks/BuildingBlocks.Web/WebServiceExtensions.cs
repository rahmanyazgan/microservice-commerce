using System.Text.Json.Serialization;
using BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BuildingBlocks.Web;

public static class WebServiceExtensions
{
    public static WebApplicationBuilder AddStandardServiceDefaults(this WebApplicationBuilder builder, string serviceName)
    {
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<GlobalProblemExceptionHandler>();
        builder.Services.AddTransient<CorrelationIdDelegatingHandler>();
        builder.Services.AddSingleton<ChaosState>();
        builder.Services.AddStandardJwtAuth(builder.Configuration);
        builder.Services.AddOpenApi();
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(o =>
                    {
                        o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
                    })
                    .AddHttpClientInstrumentation()
                    .AddSource(MessagingDiagnostics.ActivitySourceName);

                var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                }

                if (builder.Configuration.GetValue("OpenTelemetry:ConsoleExporter", false))
                {
                    tracing.AddConsoleExporter();
                }
            });

        return builder;
    }

    public static WebApplication UseStandardServicePipeline(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapOpenApi("/openapi/v1.json");

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = async (ctx, report) =>
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsJsonAsync(new
                {
                    status = report.Status.ToString(),
                    entries = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString())
                });
            }
        });

        return app;
    }
}

