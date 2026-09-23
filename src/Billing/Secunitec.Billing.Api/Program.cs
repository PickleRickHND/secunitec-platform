// R05: core transaccional en .NET 10 con Clean Architecture; esta capa solo traduce HTTP a casos de uso.
// R04: FallbackPolicy deny-by-default y cada endpoint con su política de SecunitecPolicies (docs/PLAN.md §3.2).

using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Secunitec.Billing.Api;
using Secunitec.Billing.Application;
using Secunitec.Billing.Infrastructure;
using Secunitec.BuildingBlocks.AspNetCore.Hosting;
using Secunitec.BuildingBlocks.AspNetCore.Security;
using Secunitec.BuildingBlocks.Security;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
// R06 + límites de body y timeouts comunes a los tres servicios (etapa 1.1).
builder.ConfigureSecunitecKestrel();
// R19 (5.2): trazas gateway → billing → Postgres/Mongo, métricas del pool de Npgsql y facturas emitidas.
builder.AddSecunitecTelemetry("secunitec-billing", telemetry =>
{
    telemetry.Sources.Add("Npgsql");
    telemetry.Sources.Add("MongoDB.Driver");
    telemetry.Meters.Add("Npgsql");
    telemetry.Meters.Add(MetricasFacturacion.MeterName);
});
builder.Services.AddSecunitecDefaults();
builder.Services.AddExceptionHandler<BillingExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// TB1: a Billing solo llega el gateway por la red interna; se confía en su X-Forwarded-For para auditar la IP real.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));
});

string? testKey = builder.Configuration["Billing:TestJwtKey"];
bool testMode = builder.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(testKey);
if (testMode && Encoding.UTF8.GetByteCount(testKey!) < 32)
{
    throw new InvalidOperationException("La clave de prueba debe tener al menos 32 bytes.");
}

// El valor de .env.example es público (está en el repo): con él cualquiera firmaría tokens de cualquier tenant y rol.
if (testMode && testKey!.StartsWith("CAMBIAR", StringComparison.Ordinal))
{
    throw new InvalidOperationException("BILLING_TEST_JWT_KEY conserva el valor de ejemplo; genere una clave propia.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    if (testMode)
    {
        // Solo Development y clave explícita: permite correr Billing aislado con scripts/dev-token.py.
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "secunitec-test",
            ValidateAudience = true,
            ValidAudience = SecunitecAudiences.Billing,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(testKey!)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = SecunitecClaims.Name,
            RoleClaimType = SecunitecClaims.Role
        };
    }
    else
    {
        options.UseSecunitecIdentity(builder.Configuration, builder.Environment);
    }
});

builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuditingAuthorizationResultHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddBillingInfrastructure();
builder.Services.AddScoped<IRequestContext, HttpRequestContext>();
builder.Services.AddScoped<ContextoSeguridad>();
builder.Services.AddScoped<FacturasService>();
builder.Services.AddScoped<ClientesService>();
builder.Services.AddScoped<ObligadoService>();
builder.Services.AddScoped<AuditoriaService>();

WebApplication app = builder.Build();
app.UseForwardedHeaders();
app.UseSecunitecDefaults();
app.UseAuthentication();
app.UseAuthorization();

// Las migraciones se ejecutan una vez por despliegue; el rol billing solo tiene acceso a su base.
using (IServiceScope scope = app.Services.CreateScope())
{
    BillingDbContext db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
    await db.Database.MigrateAsync();

    // Datos de demostración solo en Development: obligado y cliente del tenant de ejemplo.
    if (app.Environment.IsDevelopment() && app.Configuration["Billing:Seed:TenantId"] is { Length: > 0 } seedTenant)
    {
        await BillingDemoSeeder.SeedAsync(
            db,
            Guid.Parse(seedTenant, CultureInfo.InvariantCulture),
            Guid.Parse(app.Configuration["Billing:Seed:ClienteId"]
                ?? throw new InvalidOperationException("Configure Billing:Seed:ClienteId."), CultureInfo.InvariantCulture),
            Hoy(),
            CancellationToken.None);
    }
}

RouteGroupBuilder api = app.MapGroup("/api/billing");

RouteGroupBuilder facturas = api.MapGroup("/facturas");
facturas.MapGet("/", ([AsParameters] FiltroFacturas filtro, FacturasService service, CancellationToken ct) =>
    service.Listar(filtro, ct)).RequireAuthorization(SecunitecPolicies.ReadInvoices);
facturas.MapGet("/{id:guid}", (Guid id, FacturasService service, CancellationToken ct) =>
    service.Obtener(id, ct)).RequireAuthorization(SecunitecPolicies.ReadInvoices);
facturas.MapPost("/", async (NuevaFactura request, FacturasService service, CancellationToken ct) =>
{
    FacturaDetalle factura = await service.Crear(request, ct);
    return Results.Created($"/api/billing/facturas/{factura.Id}", factura);
}).RequireAuthorization(SecunitecPolicies.ManageInvoices);
facturas.MapPost("/{id:guid}/lineas", (Guid id, NuevaLinea request, FacturasService service, CancellationToken ct) =>
    service.AgregarLinea(id, request, ct)).RequireAuthorization(SecunitecPolicies.ManageInvoices);
facturas.MapDelete("/{id:guid}/lineas/{lineaId:int}", (Guid id, int lineaId, FacturasService service, CancellationToken ct) =>
    service.QuitarLinea(id, lineaId, ct)).RequireAuthorization(SecunitecPolicies.ManageInvoices);
facturas.MapPost("/{id:guid}/emitir", (Guid id, FacturasService service, CancellationToken ct) =>
    service.Emitir(id, Hoy(), ct)).RequireAuthorization(SecunitecPolicies.ManageInvoices);
facturas.MapPost("/{id:guid}/anular", (Guid id, AnularFactura request, FacturasService service, CancellationToken ct) =>
    service.Anular(id, request, ct)).RequireAuthorization(SecunitecPolicies.ManageInvoices);

RouteGroupBuilder clientes = api.MapGroup("/clientes").RequireAuthorization(SecunitecPolicies.ManageClientes);
clientes.MapGet("/", ([AsParameters] FiltroClientes filtro, ClientesService service, CancellationToken ct) =>
    service.Listar(filtro, ct));
clientes.MapPost("/", async (DatosCliente request, ClientesService service, CancellationToken ct) =>
{
    ClienteDto cliente = await service.Crear(request, ct);
    return Results.Created($"/api/billing/clientes/{cliente.Id}", cliente);
});
clientes.MapPut("/{id:guid}", (Guid id, DatosCliente request, ClientesService service, CancellationToken ct) =>
    service.Actualizar(id, request, ct));

RouteGroupBuilder obligados = api.MapGroup("/obligados");
obligados.MapPost("/", async (NuevoObligado request, ObligadoService service, CancellationToken ct) =>
    Results.Created("/api/billing/obligados/actual", await service.Crear(request, ct)))
    .RequireAuthorization(SecunitecPolicies.ManageObligados);
obligados.MapGet("/actual", (ObligadoService service, CancellationToken ct) => service.ObtenerActual(ct))
    .RequireAuthorization(SecunitecPolicies.ReadInvoices);
obligados.MapPut("/actual/cai", (ActualizarCai request, ObligadoService service, CancellationToken ct) =>
    service.ActualizarCai(request, ct)).RequireAuthorization(SecunitecPolicies.ManageObligados);
obligados.MapPost("/actual/activar", (ObligadoService service, CancellationToken ct) => service.Activar(ct))
    .RequireAuthorization(SecunitecPolicies.ManageObligados);
obligados.MapPost("/actual/desactivar", (ObligadoService service, CancellationToken ct) => service.Desactivar(ct))
    .RequireAuthorization(SecunitecPolicies.ManageObligados);

api.MapGet("/auditoria", ([AsParameters] FiltroAuditoria filtro, AuditoriaService service, CancellationToken ct) =>
    service.Consultar(filtro, ct)).RequireAuthorization(SecunitecPolicies.ReadAudit);

app.Run();

// Fecha fiscal de emisión: el día calendario en Honduras (chiseled-extra incluye tzdata).
static DateOnly Hoy() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
    DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Tegucigalpa")));

public partial class Program
{
}
