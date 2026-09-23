using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using Secunitec.BuildingBlocks.Security;
using Testcontainers.MongoDb;
using Testcontainers.PostgreSql;

namespace Secunitec.Billing.Api.Tests;

/// <summary>
/// Billing real contra Postgres 17 y Mongo 8 en contenedores (Testcontainers), con el mismo script de roles de Mongo
/// que usa el compose (infra/mongo/setup): Billing se conecta como <c>billing_audit</c>, con solo insert y find.
/// - Tokens firmados con una llave de prueba; el issuer imita al gateway.
/// - Redis inalcanzable: la caché se salta sin errores (R18).
/// - Las peticiones llegan desde 10.0.0.2 (el "gateway"): Billing acepta su X-Forwarded-For.
/// Sin Docker los tests se saltan en local y fallan en CI (CI=true).
/// </summary>
public sealed class BillingFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Issuer = "https://gateway.test/";
    public const string BillingMongoPassword = "clave-de-prueba-billing-audit";
    public const string IdentityMongoPassword = "clave-de-prueba-identity-audit";

    private static readonly SymmetricSecurityKey _signingKey = new(Encoding.UTF8.GetBytes("llave-de-prueba-de-billing-de-al-menos-32-bytes"));
    private static readonly IPAddress _gatewayIp = IPAddress.Parse("10.0.0.2");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    // rseq=1: MongoDB 8 no arranca con kernels 6.19 a 7.0.13 (docs/problemas-conocidos.md §1).
    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:8")
        .WithEnvironment("GLIBC_TUNABLES", "glibc.pthread.rseq=1")
        .Build();

    public string? SkipReason { get; private set; }

    /// <summary>Base de auditoría con el usuario root del contenedor, para las aserciones.</summary>
    public IMongoDatabase AuditDb { get; private set; } = null!;

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        try
        {
            await Task.WhenAll(_postgres.StartAsync(), _mongo.StartAsync());
        }
        catch (Exception ex) when (!IsCi())
        {
            SkipReason = "Docker no está disponible para Testcontainers: " + ex.Message;
            return;
        }

        await RunMongoSetupAsync();
        AuditDb = new MongoClient(_mongo.GetConnectionString()).GetDatabase("secunitec_audit");
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _mongo.DisposeAsync();
    }

    /// <summary>Corre infra/mongo/setup/secunitec-setup.js dentro del contenedor, como el servicio mongo-setup.</summary>
    public async Task RunMongoSetupAsync()
    {
        string script = Path.Combine(RepoRoot(), "infra", "mongo", "setup", "secunitec-setup.js");
        await _mongo.CopyAsync(await File.ReadAllBytesAsync(script), "/tmp/secunitec-setup.js");
        var result = await _mongo.ExecAsync(["sh", "-c",
            "MONGO_SETUP_HOST=localhost MONGO_ADMIN_USER=mongo MONGO_ADMIN_PASSWORD=mongo " +
            $"MONGO_BILLING_PASSWORD={BillingMongoPassword} MONGO_IDENTITY_PASSWORD={IdentityMongoPassword} " +
            "mongosh --nodb --quiet --file /tmp/secunitec-setup.js"]);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("mongo-setup falló: " + result.Stdout + result.Stderr);
        }
    }

    /// <summary>Cadena de conexión con un usuario de la aplicación.</summary>
    public string MongoConnectionString(string user, string password) =>
        $"mongodb://{user}:{password}@{_mongo.Hostname}:{_mongo.GetMappedPublicPort(27017)}/secunitec_audit?authSource=secunitec_audit";

    public HttpClient Client(string? token = null, string? forwardedFor = null)
    {
        HttpClient client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (forwardedFor is not null)
        {
            client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);
        }

        return client;
    }

    public static string Token(Guid tenant, string role, Guid? subject = null, Guid? clienteId = null)
    {
        List<Claim> claims =
        [
            new(SecunitecClaims.Subject, (subject ?? Guid.NewGuid()).ToString()),
            new(SecunitecClaims.Role, role),
            new(SecunitecClaims.TenantId, tenant.ToString()),
        ];
        if (clienteId is Guid cliente)
        {
            claims.Add(new Claim(SecunitecClaims.ClienteId, cliente.ToString()));
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = SecunitecAudiences.Billing,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256),
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Billing", _postgres.GetConnectionString());
        builder.UseSetting("Mongo:ConnectionString", MongoConnectionString("billing_audit", BillingMongoPassword));
        builder.UseSetting("Redis:ConnectionString", "127.0.0.1:1,abortConnect=false,connectTimeout=100,connectRetry=0,asyncTimeout=200,syncTimeout=200");
        builder.UseSetting("Jwt:Authority", Issuer);

        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                OpenIdConnectConfiguration configuration = new() { Issuer = Issuer };
                configuration.SigningKeys.Add(_signingKey);
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });
            services.AddSingleton<IStartupFilter, GatewayIpStartupFilter>();
        });
    }

    public static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Secunitec.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    private static bool IsCi() =>
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    private sealed class GatewayIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = _gatewayIp;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
