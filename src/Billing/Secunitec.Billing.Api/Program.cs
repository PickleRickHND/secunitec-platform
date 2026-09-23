using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using Secunitec.Billing.Api;
using Secunitec.Billing.Application;
using Secunitec.Billing.Infrastructure;
using Secunitec.BuildingBlocks.AspNetCore.Hosting;
using Secunitec.BuildingBlocks.AspNetCore.Security;
using Secunitec.BuildingBlocks.Security;
using StackExchange.Redis;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
// R06 + límites de body y timeouts comunes a los tres servicios (etapa 1.1).
builder.ConfigureSecunitecKestrel();
builder.Services.AddSecunitecDefaults();
builder.Services.AddExceptionHandler<BillingExceptionHandler>();

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
        // Solo Development y clave explícita: facilita 2.2 mientras Identity se construye en 3.1.
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

string pg = builder.Configuration.GetConnectionString("Billing")
    ?? throw new InvalidOperationException("Configure ConnectionStrings:Billing.");
builder.Services.AddDbContext<BillingDbContext>(options => options.UseNpgsql(pg));
string mongo = builder.Configuration["Mongo:ConnectionString"]
    ?? throw new InvalidOperationException("Configure Mongo:ConnectionString.");
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongo));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase("secunitec_audit"));
string redis = builder.Configuration["Redis:ConnectionString"]
    ?? throw new InvalidOperationException("Configure Redis:ConnectionString.");
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));
builder.Services.AddScoped<IBillingStore, EfBillingStore>();
builder.Services.AddScoped<IBillingAudit, MongoBillingAudit>();
builder.Services.AddScoped<IInvoiceCache, RedisInvoiceCache>();
builder.Services.AddScoped<BillingService>();

WebApplication app = builder.Build();
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
            DateOnly.FromDateTime(DateTime.UtcNow),
            CancellationToken.None);
    }
}

RouteGroupBuilder api = app.MapGroup("/api/billing");
api.MapPost("/obligados", async (NuevoObligado request, BillingService service, CancellationToken ct) =>
    Results.Created("/api/billing/obligados", await service.CrearObligado(request, ct)))
    .RequireAuthorization(SecunitecPolicies.ManageObligados);
api.MapPost("/clientes", async (NuevoCliente request, BillingService service, CancellationToken ct) =>
{
    var cliente = await service.CrearCliente(request, ct);
    return Results.Created($"/api/billing/clientes/{cliente.Id}", cliente);
})
    .RequireAuthorization(SecunitecPolicies.ManageClientes);
api.MapPost("/facturas", async (NuevaFactura request, BillingService service, CancellationToken ct) =>
{
    var factura = await service.CrearFactura(request, ct);
    return Results.Created($"/api/billing/facturas/{factura.Id}", factura);
}).RequireAuthorization(SecunitecPolicies.ManageInvoices);
api.MapPost("/facturas/{id:guid}/emitir", async (Guid id, BillingService service, CancellationToken ct) =>
    Results.Ok(await service.Emitir(id, DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
        DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Tegucigalpa"))), ct)))
    .RequireAuthorization(SecunitecPolicies.ManageInvoices);
api.MapPost("/facturas/{id:guid}/anular", async (Guid id, BillingService service, CancellationToken ct) =>
    Results.Ok(await service.Anular(id, ct)))
    .RequireAuthorization(SecunitecPolicies.ManageInvoices);
api.MapGet("/facturas", async (BillingService service, CancellationToken ct) =>
    Results.Ok(await service.Listar(ct)))
    .RequireAuthorization(SecunitecPolicies.ReadInvoices);
api.MapGet("/facturas/{id:guid}", async (Guid id, BillingService service, CancellationToken ct) =>
    Results.Ok(await service.Obtener(id, ct)))
    .RequireAuthorization(SecunitecPolicies.ReadInvoices);

app.Run();

public partial class Program
{
}
