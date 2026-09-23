// R03: esta fábrica centraliza la construcción de los claims del contrato Secunitec.
// R04: tenant_id y cliente_id acompañan al principal para mantener la autorización contextual.

using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using Secunitec.BuildingBlocks.Security;

namespace Secunitec.Identity;

public sealed class IdentityPrincipalFactory
{
    private readonly UserManager<ApplicationUser> _users;

    public IdentityPrincipalFactory(UserManager<ApplicationUser> users)
    {
        _users = users;
    }

    public async Task<ClaimsPrincipal> CreateUserAsync(
        ApplicationUser user,
        string? clientId,
        IEnumerable<string> scopes)
    {
        var roles = await _users.GetRolesAsync(user);

        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType,
            OpenIddictConstants.Claims.Name,
            OpenIddictConstants.Claims.Role);

        identity.SetClaim(OpenIddictConstants.Claims.Subject, user.Id.ToString());
        identity.SetClaim(OpenIddictConstants.Claims.Name, user.UserName ?? user.Id.ToString());
        identity.SetClaim(SecunitecClaims.TenantId, user.TenantId.ToString());

        if (user.Email is not null)
        {
            identity.SetClaim(OpenIddictConstants.Claims.Email, user.Email);
        }

        if (user.ClienteId is Guid clienteId)
        {
            identity.SetClaim(SecunitecClaims.ClienteId, clienteId.ToString());
        }

        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(SecunitecClaims.Role, role));
        }

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            identity.SetClaim(SecunitecClaims.ClientId, clientId);
        }

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(scopes);

        if (scopes.Contains(SecunitecAudiences.Billing, StringComparer.Ordinal))
        {
            principal.SetResources(SecunitecAudiences.Billing);
        }

        // R09: con el scope roles, el id_token también lleva rol, tenant y cliente para que el SPA muestre la UI de
        // cada rol sin leer el access token (que es para Billing). La autorización se sigue decidiendo en servidor.
        bool rolesInIdToken = principal.HasScope(OpenIddictConstants.Scopes.Roles);
        principal.SetDestinations(claim => claim.Type switch
        {
            OpenIddictConstants.Claims.Subject or OpenIddictConstants.Claims.Name or OpenIddictConstants.Claims.Email =>
                [OpenIddictConstants.Destinations.AccessToken,
                 OpenIddictConstants.Destinations.IdentityToken],

            SecunitecClaims.Role or SecunitecClaims.TenantId or SecunitecClaims.ClienteId when rolesInIdToken =>
                [OpenIddictConstants.Destinations.AccessToken,
                 OpenIddictConstants.Destinations.IdentityToken],

            _ => [OpenIddictConstants.Destinations.AccessToken]
        });

        return principal;
    }

    /// <summary>
    /// Principal de una aplicación (client credentials). El <c>sub</c> es el GUID de la aplicación en OpenIddict:
    /// el contrato exige un GUID (docs/PLAN.md §7.1) y Billing lo registra como actor de cada escritura.
    /// </summary>
    public ClaimsPrincipal CreateClient(
        Guid applicationId,
        string clientId,
        Guid tenantId,
        string role,
        IEnumerable<string> scopes)
    {
        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType,
            OpenIddictConstants.Claims.Name,
            OpenIddictConstants.Claims.Role);

        identity.SetClaim(OpenIddictConstants.Claims.Subject, applicationId.ToString());
        identity.SetClaim(OpenIddictConstants.Claims.Name, clientId);
        identity.SetClaim(SecunitecClaims.ClientId, clientId);
        identity.SetClaim(SecunitecClaims.TenantId, tenantId.ToString());
        identity.AddClaim(new Claim(SecunitecClaims.Role, role));

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(scopes);

        if (scopes.Contains(SecunitecAudiences.Billing, StringComparer.Ordinal))
        {
            principal.SetResources(SecunitecAudiences.Billing);
        }

        principal.SetDestinations(static claim => claim.Type switch
        {
            OpenIddictConstants.Claims.Subject or OpenIddictConstants.Claims.Name =>
                [OpenIddictConstants.Destinations.AccessToken,
                 OpenIddictConstants.Destinations.IdentityToken],

            _ => [OpenIddictConstants.Destinations.AccessToken]
        });

        return principal;
    }
}
