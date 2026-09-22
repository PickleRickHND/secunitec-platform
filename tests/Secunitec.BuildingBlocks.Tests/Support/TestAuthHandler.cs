using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Secunitec.BuildingBlocks.Tests.Support;

/// <summary>
/// Esquema de autenticación de prueba: si llega la cabecera <c>X-Test-Roles</c> autentica con esos roles y el
/// tenant de <see cref="Principals"/>; <c>X-Test-No-Tenant</c> omite el tenant. Sin cabecera, la petición es anónima.
/// </summary>
internal sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? roles = Request.Headers["X-Test-Roles"].FirstOrDefault();
        if (roles is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        bool withoutTenant = Request.Headers.ContainsKey("X-Test-No-Tenant");
        ClaimsPrincipal principal = Principals.Build(
            withoutTenant ? null : Principals.Tenant,
            roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        AuthenticationTicket ticket = new(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

internal static class TestAuthExtensions
{
    public static IServiceCollection AddTestAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        return services;
    }

    public static HttpRequestMessage AsRoles(this HttpRequestMessage request, params string[] roles)
    {
        request.Headers.Add("X-Test-Roles", string.Join(',', roles));
        return request;
    }
}
