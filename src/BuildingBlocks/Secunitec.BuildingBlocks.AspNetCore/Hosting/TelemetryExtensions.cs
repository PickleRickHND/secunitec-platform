using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Secunitec.BuildingBlocks.AspNetCore.Hosting;

/// <summary>Opciones por servicio de <see cref="TelemetryExtensions.AddSecunitecTelemetry"/>.</summary>
public sealed class SecunitecTelemetryOptions
{
    /// <summary>Fuentes de trazas (<c>ActivitySource</c>) propias del servicio, además de ASP.NET Core y HttpClient.</summary>
    public IList<string> Sources { get; } = [];

    /// <summary>Meters propios del servicio, además de los de ASP.NET Core, HttpClient y el runtime.</summary>
    public IList<string> Meters { get; } = [];

    /// <summary>
    /// TB0: descarta el contexto de traza (<c>traceparent</c>, <c>tracestate</c>, <c>baggage</c>) que llega del
    /// cliente. Solo para el servicio expuesto a Internet (el gateway): un cliente no elige el trace id ni inyecta
    /// baggage en los servicios internos. Hacia adentro el contexto se sigue propagando.
    /// </summary>
    public bool IgnoreIncomingTraceContext { get; set; }
}

/// <summary>
/// Fase 4 (R19): trazas, métricas y logs con OpenTelemetry, exportados por OTLP al collector
/// (docker-compose.observability.yml). Sin <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> no se exporta nada: los tests y el
/// compose sin observabilidad no intentan conectarse.
/// </summary>
public static class TelemetryExtensions
{
    /// <summary>Variable estándar de OpenTelemetry con la URL del collector (p. ej. <c>http://otel-collector:4317</c>).</summary>
    public const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>Fracción de trazas raíz que se muestrean (0 a 1). Por defecto, todas.</summary>
    public const string TraceSampleRatioKey = "Telemetry:TraceSampleRatio";

    /// <summary>Meters integrados de .NET y ASP.NET Core que alimentan los dashboards USE y de resiliencia.</summary>
    private static readonly string[] _builtInMeters =
    [
        "System.Runtime",                       // thread pool, GC, CPU del proceso (Método USE)
        "Microsoft.AspNetCore.RateLimiting",    // permisos concedidos y rechazados por el middleware
        "Microsoft.AspNetCore.Diagnostics",     // excepciones no controladas
    ];

    public static WebApplicationBuilder AddSecunitecTelemetry(
        this WebApplicationBuilder builder,
        string serviceName,
        Action<SecunitecTelemetryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        SecunitecTelemetryOptions options = new();
        configure?.Invoke(options);

        if (options.IgnoreIncomingTraceContext)
        {
            // Dos lectores del traceparent entrante: el hosting de ASP.NET Core (propagador del contenedor de DI) y la
            // instrumentación de OpenTelemetry (propagador global), que crea su propia actividad si el global no es
            // solo W3C. Ambos se reemplazan por versiones que no extraen pero sí inyectan.
            builder.Services.AddSingleton<System.Diagnostics.DistributedContextPropagator>(new EdgeDistributedContextPropagator());
            Sdk.SetDefaultTextMapPropagator(new EdgeTextMapPropagator(
                new CompositeTextMapPropagator([new TraceContextPropagator(), new BaggagePropagator()])));
        }

        double sampleRatio = Math.Clamp(builder.Configuration.GetValue(TraceSampleRatioKey, 1.0), 0.0, 1.0);
        string? version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString();

        OpenTelemetryBuilder telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: version)
                .AddAttributes([new("deployment.environment.name", builder.Environment.EnvironmentName)]))
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(sampleRatio)))
                    // Los health checks no aportan a las trazas y llenarían Tempo. La query string ya llega redactada.
                    .AddAspNetCoreInstrumentation(aspnet => aspnet.Filter = context => !IsHealthCheck(context.Request))
                    .AddHttpClientInstrumentation();
                foreach (string source in options.Sources)
                {
                    tracing.AddSource(source);
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddMeter(_builtInMeters);
                foreach (string meter in options.Meters)
                {
                    metrics.AddMeter(meter);
                }
            })
            // IncludeScopes: el CorrelationId del scope de log viaja a Loki junto al trace id.
            .WithLogging(configureBuilder: null, configureOptions: logging =>
            {
                logging.IncludeScopes = true;
                logging.IncludeFormattedMessage = true;
            });

        if (!string.IsNullOrWhiteSpace(builder.Configuration[OtlpEndpointKey]))
        {
            // Lee endpoint y protocolo de las variables OTEL_EXPORTER_OTLP_* (gRPC por defecto).
            telemetry.UseOtlpExporter();
        }

        return builder;
    }

    private static bool IsHealthCheck(HttpRequest request) =>
        request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase) ||
        request.Path.StartsWithSegments("/healthz", StringComparison.OrdinalIgnoreCase);
}
