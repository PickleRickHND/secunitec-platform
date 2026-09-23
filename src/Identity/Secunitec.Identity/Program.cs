// R03: Identity implementa OAuth 2.0 / OIDC: emisión de JWT RS256, discovery y JWKS.
// R04: deny-by-default; solo el protocolo (páginas de cuenta, /connect y discovery) admite anónimos.
// A07: lockout tras 5 intentos, contraseñas robustas, login y registro auditados.

using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Secunitec.BuildingBlocks.AspNetCore.Hosting;
using Secunitec.BuildingBlocks.Security;
using Secunitec.Identity;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.ConfigureSecunitecKestrel();
// CSP propia: las páginas de cuenta solo cargan recursos del mismo origen.
builder.Services.AddSecunitecDefaults(headers =>
    headers.ContentSecurityPolicy = "default-src 'self'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'");

// TB1: a Identity solo llegan el gateway, Billing y el gateway por el backchannel, por redes internas. Se confía
// en sus X-Forwarded-* para conocer la IP real del cliente (auditoría, lockout), el esquema https con el que
// llegó al gateway (cookies Secure) y el host público con el que OpenIddict arma las URLs del discovery.
// X-Forwarded-Host solo se acepta si coincide con el host del issuer (AllowedHosts compara el host, sin puerto).
builder.Services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((options, configuration) =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));
    string? issuer = configuration["Identity:Issuer"];
    if (issuer is not null)
    {
        options.AllowedHosts.Add(new Uri(issuer).Host);
    }
});

// La configuración se lee al resolver los servicios (no al registrarlos) para que los tests la puedan reemplazar.
builder.Services.AddDbContext<ApplicationDbContext>((services, options) =>
{
    options.UseNpgsql(services.GetRequiredService<IConfiguration>().GetConnectionString("Identity")
        ?? throw new InvalidOperationException("Configure ConnectionStrings:Identity."));
    options.UseOpenIddict<Guid>();
});

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddErrorDescriber<SpanishIdentityErrorDescriber>();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
        options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
    })
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        // __Host-: solo HTTPS, sin Domain y con Path=/. El TLS termina en el gateway (TB0) y Identity ve https
        // por X-Forwarded-Proto; sin Secure los navegadores descartan cualquier cookie __Host-.
        options.Cookie.Name = "__Host-Secunitec.Identity";
        options.Cookie.Path = "/";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(1);
        options.SlidingExpiration = true;
        options.LoginPath = "/account/login";
    });

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "__Host-Secunitec.Antiforgery";
    options.Cookie.Path = "/";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

// Login y registro son Razor Pages: validan el token antiforgery solas (400 si falta) y son anónimas.
builder.Services.AddRazorPages(options => options.Conventions.AllowAnonymousToFolder("/Account"));

builder.Services.AddSingleton<IMongoClient>(services => new MongoClient(
    services.GetRequiredService<IConfiguration>()["Mongo:ConnectionString"]
        ?? throw new InvalidOperationException("Configure Mongo:ConnectionString.")));
builder.Services.AddSingleton(services =>
    services.GetRequiredService<IMongoClient>().GetDatabase("secunitec_audit"));
builder.Services.AddSingleton<IdentityAuditWriter>();
builder.Services.AddScoped<IdentityPrincipalFactory>();

builder.Services
    .AddOpenIddict()
    .AddCore(options => options
        .UseEntityFrameworkCore()
        .UseDbContext<ApplicationDbContext>()
        .ReplaceDefaultEntities<Guid>())
    .AddServer(options =>
    {
        options.SetAuthorizationEndpointUris("connect/authorize")
               .SetTokenEndpointUris("connect/token");

        options.AllowAuthorizationCodeFlow()
               .AllowRefreshTokenFlow()
               .AllowClientCredentialsFlow();

        // PKCE obligatorio (OAuth 2.0 Security BCP, RFC 9700).
        options.RequireProofKeyForCodeExchange();

        options.RegisterScopes(
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Scopes.Roles,
            OpenIddictConstants.Scopes.OfflineAccess,
            SecunitecAudiences.Billing);

        // JWT firmado (RS256), no cifrado: Billing y el Gateway lo validan con el JWKS.
        options.DisableAccessTokenEncryption();
        options.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));
        options.SetRefreshTokenLifetime(TimeSpan.FromDays(7));

        AddCredentials(options, builder.Configuration, builder.Environment);

        options.UseAspNetCore()
               .EnableAuthorizationEndpointPassthrough()
               .EnableTokenEndpointPassthrough()
               // El TLS termina en el gateway (TB0) y Billing y el Gateway leen discovery y JWKS por la red
               // interna (TB1), sin TLS.
               .DisableTransportSecurityRequirement();
    });

// Issuer público: la URL del gateway que ve el navegador. Así el discovery anuncia endpoints alcanzables desde
// afuera; Billing y el Gateway descargan el JWKS por la red interna (InternalAuthorityHandler).
builder.Services.AddOptions<OpenIddictServerOptions>().Configure<IConfiguration>((options, configuration) =>
    options.Issuer = new Uri(configuration["Identity:Issuer"]
        ?? throw new InvalidOperationException("Configure Identity:Issuer con la URL pública del gateway.")));

WebApplication app = builder.Build();

app.UseForwardedHeaders();
app.UseSecunitecDefaults();
app.UseAuthentication();
app.UseAuthorization();

await IdentitySeeder.SeedAsync(app.Services, app.Configuration);

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "identity" })).AllowAnonymous();
app.MapRazorPages();
app.MapConnectEndpoints();
app.MapGet("/", () => Results.Redirect("/account/login")).AllowAnonymous();

await app.RunAsync();

// Development usa llaves efímeras en memoria: cambian al reiniciar y no tocan el almacén de certificados del
// equipo. Fuera de Development se exigen certificados PEM configurados.
static void AddCredentials(OpenIddictServerBuilder options, IConfiguration configuration, IWebHostEnvironment environment)
{
    if (environment.IsDevelopment())
    {
        options.AddEphemeralSigningKey().AddEphemeralEncryptionKey();
        return;
    }

    options.AddSigningCertificate(LoadPem(configuration, "Identity:SigningCertificate"));
    options.AddEncryptionCertificate(LoadPem(configuration, "Identity:EncryptionCertificate"));
}

static X509Certificate2 LoadPem(IConfiguration configuration, string section) =>
    X509Certificate2.CreateFromPemFile(
        configuration[$"{section}:Path"] ?? throw new InvalidOperationException($"Configure {section}:Path."),
        configuration[$"{section}:KeyPath"] ?? throw new InvalidOperationException($"Configure {section}:KeyPath."));

public partial class Program
{
}
