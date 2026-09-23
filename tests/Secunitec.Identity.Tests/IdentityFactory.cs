using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Testcontainers.PostgreSql;

namespace Secunitec.Identity.Tests;

/// <summary>
/// Identity real con Postgres 17 y Mongo 8 en contenedores (Testcontainers). Sin Docker, los tests se saltan en
/// local y fallan en CI (variable CI=true), para que la cobertura nunca se pierda en silencio.
/// </summary>
public sealed class IdentityFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Issuer = "https://localhost/";
    public const string AdminEmail = "admin@secunitec.test";
    public const string AdminPassword = "Admin!Prueba123";
    public const string JmeterSecret = "secreto-de-prueba-de-jmeter-load";
    public static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-00000000aa01");

    // IP con la que llega el "gateway" en los tests: red privada, así Identity acepta sus X-Forwarded-*.
    private static readonly IPAddress _gatewayIp = IPAddress.Parse("10.0.0.2");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    // rseq=1: MongoDB 8 no arranca con kernels 6.19 a 7.0.13 (docs/problemas-conocidos.md). No cambia nada en
    // kernels no afectados, salvo algo de rendimiento del asignador.
    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:8")
        .WithEnvironment("GLIBC_TUNABLES", "glibc.pthread.rseq=1")
        .Build();

    public string? SkipReason { get; private set; }

    public IMongoCollection<BsonDocument> AuditEvents { get; private set; } = null!;

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

        AuditEvents = new MongoClient(_mongo.GetConnectionString())
            .GetDatabase("secunitec_audit")
            .GetCollection<BsonDocument>("identity_events");
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _mongo.DisposeAsync();
    }

    public HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri(Issuer),
        AllowAutoRedirect = false,
    });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Identity", _postgres.GetConnectionString());
        builder.UseSetting("Mongo:ConnectionString", _mongo.GetConnectionString());
        builder.UseSetting("Identity:Issuer", Issuer);
        builder.UseSetting("Identity:RegistrationTenantId", TenantId.ToString());
        builder.UseSetting("Identity:SeedAdminEmail", AdminEmail);
        builder.UseSetting("Identity:SeedAdminPassword", AdminPassword);
        builder.UseSetting("Identity:SeedAdminTenantId", TenantId.ToString());
        builder.UseSetting("Identity:JmeterClientSecret", JmeterSecret);
        builder.UseSetting("Identity:JmeterTenantId", TenantId.ToString());

        builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, GatewayIpStartupFilter>());
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
