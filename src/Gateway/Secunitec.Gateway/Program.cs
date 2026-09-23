// R01: YARP es la única entrada pública y enruta hacia Identity y Billing.
// R02: rate limiting L7 con contadores en Redis antes de que la petición llegue a un servicio interno.
// R04: solo las rutas del protocolo OIDC son anónimas; el resto exige un token con tenant_id.
// R06: sin Server ni X-Powered-By, ni del gateway ni de las respuestas reenviadas.
// TB0: el TLS termina aquí (certificado de desarrollo montado por el compose; ver scripts/dev-cert.sh).
// R09: CORS solo para el origen del SPA (etapa 4.1); cada ruta de YARP declara si lo admite (CorsPolicy).
// R19: OpenTelemetry (etapa 5.2); el rate limiting publica secunitec_gateway_rate_limited_total.

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Secunitec.BuildingBlocks.AspNetCore.Hosting;
using Secunitec.BuildingBlocks.AspNetCore.Http;
using Secunitec.BuildingBlocks.AspNetCore.Security;
using Secunitec.BuildingBlocks.Http;
using Secunitec.Gateway;
using StackExchange.Redis;
using Yarp.ReverseProxy.Transforms;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.ConfigureSecunitecKestrel();
// R19 (5.2): trazas, métricas y logs por OTLP. TB0: el gateway no acepta el contexto de traza que manda el cliente.
builder.AddSecunitecTelemetry("secunitec-gateway", telemetry =>
{
    telemetry.IgnoreIncomingTraceContext = true;
    telemetry.Sources.Add("Yarp.ReverseProxy");
    telemetry.Meters.Add(GatewayMetrics.MeterName);
});
builder.Services.AddSecunitecDefaults();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.UseSecunitecIdentity(builder.Configuration, builder.Environment));

// §4: HSTS en las respuestas HTTPS. UseHsts excluye localhost: ahí fijaría HTTPS en todos los puertos de la máquina.
builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(180));

// abortConnect=false: si Redis no está disponible al arrancar, el gateway arranca igual y limita en memoria.
builder.Services.AddSingleton<IConnectionMultiplexer>(sp => ConnectionMultiplexer.Connect(
    sp.GetRequiredService<IConfiguration>()["Redis:ConnectionString"]
        ?? throw new InvalidOperationException("Configure Redis:ConnectionString.")));
builder.Services.AddSecunitecRateLimiting(builder.Configuration);
builder.Services.AddSecunitecCors(builder.Configuration);

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(context => context.AddRequestTransform(transform =>
    {
        // El correlation id validado (o generado) por el gateway viaja a los servicios internos.
        string? correlationId = transform.HttpContext.GetCorrelationId();
        if (correlationId is not null)
        {
            transform.ProxyRequest.Headers.Remove(CorrelationId.HeaderName);
            transform.ProxyRequest.Headers.TryAddWithoutValidation(CorrelationId.HeaderName, correlationId);
        }

        return ValueTask.CompletedTask;
    }));

WebApplication app = builder.Build();

app.UseSecunitecDefaults();
app.UseHsts();
// Antes de autenticar y limitar: el preflight se responde aquí sin gastar cupo ni llegar a un servicio, y los 401 y
// 429 llevan Access-Control-Allow-Origin para que el SPA pueda leer su código y el Retry-After.
app.UseCors();
app.UseAuthentication();
// R02: después de autenticar (para particionar por sub) y antes de autorizar, para que las ráfagas con tokens
// inválidos también se limiten por IP en vez de pasar directo al 401.
app.UseRateLimiter();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "gateway" }))
    .AllowAnonymous()
    .RequireRateLimiting(GatewayRateLimitPolicies.AnonymousByIp);

// Cada ruta declara su AuthorizationPolicy y su RateLimiterPolicy en appsettings.json (ReverseProxy:Routes).
app.MapReverseProxy();

await app.RunAsync();

public partial class Program
{
}
