using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Secunitec.BuildingBlocks.Security;

namespace Secunitec.BuildingBlocks.AspNetCore.Security;

/// <summary>Validación de los tokens de Identity, común a Billing y al Gateway.</summary>
public static class SecunitecJwtBearerExtensions
{
    /// <summary>
    /// Valida firma (JWKS de Identity), <c>iss</c>, <c>aud</c> = <see cref="SecunitecAudiences.Billing"/> y
    /// vigencia, con los claims crudos del contrato (docs/PLAN.md §7.1).
    /// </summary>
    /// <remarks>
    /// <c>Jwt:Authority</c> es el issuer público. Si hay <c>Jwt:InternalAuthority</c>, la metadata y el JWKS se
    /// descargan por esa dirección interna (ver <see cref="InternalAuthorityHandler"/>).
    /// </remarks>
    public static JwtBearerOptions UseSecunitecIdentity(
        this JwtBearerOptions options,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        string authority = configuration["Jwt:Authority"]
            ?? throw new InvalidOperationException("Configure Jwt:Authority (issuer público de Identity).");

        options.Authority = authority;
        options.Audience = SecunitecAudiences.Billing;
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = !environment.IsDevelopment();
        // Ante un kid desconocido (Identity reiniciado con llaves nuevas) se vuelve a pedir el JWKS; por defecto
        // JwtBearer espera 5 min entre refrescos y los tokens nuevos se rechazarían ese tiempo.
        options.RefreshInterval = TimeSpan.FromSeconds(30);

        string? internalAuthority = configuration["Jwt:InternalAuthority"];
        if (!string.IsNullOrWhiteSpace(internalAuthority))
        {
            options.BackchannelHttpHandler = new InternalAuthorityHandler(
                new Uri(authority), new Uri(internalAuthority), new HttpClientHandler());
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            NameClaimType = SecunitecClaims.Name,
            RoleClaimType = SecunitecClaims.Role,
        };

        return options;
    }
}
