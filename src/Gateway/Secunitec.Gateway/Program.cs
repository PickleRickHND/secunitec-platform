// R01: YARP es la única entrada pública y enruta hacia Identity y Billing.
// R02: rate limiting L7 con contadores en Redis antes de que la petición llegue a un servicio interno.
// R04: solo las rutas del protocolo OIDC son anónimas; el resto exige un token con tenant_id.
// R06: sin Server ni X-Powered-By, ni del gateway ni de las respuestas reenviadas.

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Secunitec.BuildingBlocks.AspNetCore.Hosting;
using Secunitec.BuildingBlocks.AspNetCore.Http;
using Secunitec.BuildingBlocks.Http;
using Secunitec.BuildingBlocks.Security;
using Secunitec.Gateway;
using StackExchange.Redis;
using Yarp.ReverseProxy.Transforms;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.ConfigureSecunitecKestrel();
builder.Services.AddSecunitecDefaults();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.Authority = builder.Configuration["Jwt:Authority"]
            ?? throw new InvalidOperationException("Configure Jwt:Authority.");
        options.Audience = SecunitecAudiences.Billing;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            NameClaimType = SecunitecClaims.Name,
            RoleClaimType = SecunitecClaims.Role,
        };
    });

// abortConnect=false: si Redis no está disponible al arrancar, el gateway arranca igual y limita en memoria.
builder.Services.AddSingleton<IConnectionMultiplexer>(sp => ConnectionMultiplexer.Connect(
    sp.GetRequiredService<IConfiguration>()["Redis:ConnectionString"]
        ?? throw new InvalidOperationException("Configure Redis:ConnectionString.")));
builder.Services.AddSecunitecRateLimiting(builder.Configuration);

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
