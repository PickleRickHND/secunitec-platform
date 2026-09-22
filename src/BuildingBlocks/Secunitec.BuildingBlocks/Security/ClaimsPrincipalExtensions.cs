using System.Security.Claims;

namespace Secunitec.BuildingBlocks.Security;

/// <summary>
/// Lectura tolerante del contrato del token: un claim ausente o mal formado devuelve <c>null</c>,
/// nunca lanza. La decisión de rechazar la petición corresponde a las políticas de autorización.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>Claim <c>sub</c> como Guid.</summary>
    public static Guid? GetUserId(this ClaimsPrincipal principal) => principal.GetGuidClaim(SecunitecClaims.Subject);

    /// <summary>Claim <c>tenant_id</c> como Guid.</summary>
    public static Guid? GetTenantId(this ClaimsPrincipal principal) => principal.GetGuidClaim(SecunitecClaims.TenantId);

    /// <summary>Claim <c>cliente_id</c> como Guid, únicamente si el principal tiene el rol <see cref="SecunitecRoles.Cliente"/>.</summary>
    public static Guid? GetClienteId(this ClaimsPrincipal principal) =>
        principal.HasSecunitecRole(SecunitecRoles.Cliente) ? principal.GetGuidClaim(SecunitecClaims.ClienteId) : null;

    /// <summary>Claim <c>client_id</c> (aplicación OAuth).</summary>
    public static string? GetClientId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        string? value = principal.FindFirst(SecunitecClaims.ClientId)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Todos los valores del claim <c>role</c>, sin duplicados.</summary>
    public static IReadOnlyCollection<string> GetRoles(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.FindAll(SecunitecClaims.Role)
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Indica si el principal tiene el rol dado en el claim <c>role</c> (no depende del RoleClaimType del handler).</summary>
    public static bool HasSecunitecRole(this ClaimsPrincipal principal, string role)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        return principal.HasClaim(c => string.Equals(c.Type, SecunitecClaims.Role, StringComparison.Ordinal)
                                       && string.Equals(c.Value, role, StringComparison.Ordinal));
    }

    private static Guid? GetGuidClaim(this ClaimsPrincipal principal, string claimType)
    {
        ArgumentNullException.ThrowIfNull(principal);
        string? value = principal.FindFirst(claimType)?.Value;
        return Guid.TryParse(value, out Guid parsed) && parsed != Guid.Empty ? parsed : null;
    }
}
