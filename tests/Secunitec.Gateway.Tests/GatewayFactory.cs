using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Secunitec.BuildingBlocks.Security;

namespace Secunitec.Gateway.Tests;

/// <summary>
/// Gateway real en memoria con:
/// - JWT firmados con una llave de prueba (sin Identity);
/// - Redis inalcanzable, así que el rate limiting usa el respaldo en memoria (el mismo camino que ante una caída);
/// - un backend falso en Kestrel que registra lo que recibe y agrega Server y X-Powered-By para comprobar que el
///   gateway los quita.
/// La cabecera de prueba X-Test-Ip fija la IP remota para aislar las particiones del rate limiting entre tests.
/// </summary>
public sealed class GatewayFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Issuer = "https://gateway.test/";
    public const int TokenEndpointLimit = 5;
    public const int AnonymousLimit = 10;
    public const int UserLimit = 10;

    private static readonly SymmetricSecurityKey _signingKey =
        new(Encoding.UTF8.GetBytes("llave-de-prueba-del-gateway-de-al-menos-32-bytes"));

    private WebApplication? _backend;

    public string BackendUrl { get; private set; } = "";

    /// <summary>Peticiones que llegaron al backend, por ruta.</summary>
    public ConcurrentDictionary<string, int> BackendHits { get; } = new();

    /// <summary>Último X-Correlation-Id que recibió el backend, por ruta.</summary>
    public ConcurrentDictionary<string, string> BackendCorrelationIds { get; } = new();

    public async ValueTask InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _backend = builder.Build();
        _backend.Map("/{**path}", (HttpContext context) =>
        {
            string path = context.Request.Path.Value ?? "/";
            BackendHits.AddOrUpdate(path, 1, (_, hits) => hits + 1);
            BackendCorrelationIds[path] = context.Request.Headers["X-Correlation-Id"].ToString();
            context.Response.Headers.Server = "Kestrel-backend";
            context.Response.Headers["X-Powered-By"] = "backend";
            return Results.Ok(new { path });
        });
        await _backend.StartAsync(TestContext.Current.CancellationToken);
        BackendUrl = _backend.Urls.Single();
    }

    public override async ValueTask DisposeAsync()
    {
        if (_backend is not null)
        {
            await _backend.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    public static string CreateToken(Guid? subject = null, bool withTenant = true, string role = SecunitecRoles.Facturador)
    {
        List<Claim> claims =
        [
            new(SecunitecClaims.Subject, (subject ?? Guid.NewGuid()).ToString()),
            new(SecunitecClaims.Role, role),
        ];
        if (withTenant)
        {
            claims.Add(new Claim(SecunitecClaims.TenantId, Guid.NewGuid().ToString()));
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
        builder.UseSetting("Jwt:Authority", Issuer);
        builder.UseSetting("Redis:ConnectionString", "127.0.0.1:1,abortConnect=false,connectTimeout=100,connectRetry=0");
        builder.UseSetting("RateLimiting:TokenEndpointPermitLimit", TokenEndpointLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("RateLimiting:AnonymousPermitLimit", AnonymousLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("RateLimiting:UserPermitLimit", UserLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("ReverseProxy:Clusters:identity:Destinations:identity:Address", BackendUrl);
        builder.UseSetting("ReverseProxy:Clusters:billing:Destinations:billing:Address", BackendUrl);

        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                OpenIdConnectConfiguration configuration = new() { Issuer = Issuer };
                configuration.SigningKeys.Add(_signingKey);
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });
            services.AddSingleton<IStartupFilter, TestIpStartupFilter>();
        });
    }

    private sealed class TestIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (IPAddress.TryParse(context.Request.Headers["X-Test-Ip"].ToString(), out IPAddress? ip))
                {
                    context.Connection.RemoteIpAddress = ip;
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
