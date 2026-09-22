namespace Secunitec.BuildingBlocks.Security;

/// <summary>
/// Nombres de los claims del contrato del token (docs/PLAN.md §3.2).
/// Se usan los nombres crudos que emite OpenIddict; ningún servicio mapea a <c>ClaimTypes.*</c>.
/// </summary>
public static class SecunitecClaims
{
    /// <summary>Identificador del usuario (Guid). Partición del rate limiter <c>user-by-sub</c>.</summary>
    public const string Subject = "sub";

    /// <summary>Rol RBAC; multivalor. Valores en <see cref="SecunitecRoles"/>.</summary>
    public const string Role = "role";

    /// <summary>Obligado tributario (empresa) al que pertenece el usuario. Obligatorio en todo token de usuario.</summary>
    public const string TenantId = "tenant_id";

    /// <summary>Cliente asociado; solo presente cuando el rol es <see cref="SecunitecRoles.Cliente"/>.</summary>
    public const string ClienteId = "cliente_id";

    /// <summary>Identificador de la aplicación cliente OAuth (por ejemplo <c>jmeter-load</c> en client credentials).</summary>
    public const string ClientId = "client_id";

    /// <summary>Nombre para mostrar del usuario.</summary>
    public const string Name = "name";

    /// <summary>Correo del usuario.</summary>
    public const string Email = "email";
}
