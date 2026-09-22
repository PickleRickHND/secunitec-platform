using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Secunitec.BuildingBlocks.Security;

namespace Secunitec.BuildingBlocks.AspNetCore.Security;

/// <summary>Registra las políticas del contrato del token y la política deny-by-default.</summary>
public static class AuthorizationExtensions
{
    /// <summary>
    /// Registra <see cref="SecunitecPolicies"/> a partir de <see cref="SecunitecPolicies.RolesByPolicy"/> y fija
    /// <c>FallbackPolicy</c> y <c>DefaultPolicy</c> a "autenticado con <c>tenant_id</c>".
    /// </summary>
    /// <remarks>
    /// R04: ningún servicio interno procesa peticiones anónimas. Cualquier endpoint sin metadatos de autorización
    /// exige token; los públicos deben declarar <c>[AllowAnonymous]</c> de forma explícita.
    /// Las políticas evalúan el claim crudo <c>role</c> para no depender del <c>RoleClaimType</c> del handler.
    /// </remarks>
    public static IServiceCollection AddSecunitecAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthorization(options =>
        {
            AuthorizationPolicy authenticatedTenantUser = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireClaim(SecunitecClaims.TenantId)
                .Build();

            // R04: deny-by-default.
            options.DefaultPolicy = authenticatedTenantUser;
            options.FallbackPolicy = authenticatedTenantUser;

            foreach ((string policy, IReadOnlyList<string> roles) in SecunitecPolicies.RolesByPolicy)
            {
                options.AddPolicy(policy, builder => builder
                    .RequireAuthenticatedUser()
                    .RequireClaim(SecunitecClaims.TenantId)
                    .RequireClaim(SecunitecClaims.Role, roles));
            }
        });

        return services;
    }
}
