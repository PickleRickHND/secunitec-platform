namespace Secunitec.BuildingBlocks.Security;

/// <summary>
/// Identidad del llamador actual leída del token. La capa de aplicación depende de esta abstracción,
/// nunca de <c>HttpContext</c>; el tenant jamás llega como parámetro del request (docs/PLAN.md §3.2).
/// </summary>
public interface ICurrentUser
{
    /// <summary>Hay un principal autenticado.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Claim <c>sub</c> como Guid; <c>null</c> si falta, es inválido o es un token de aplicación.</summary>
    Guid? UserId { get; }

    /// <summary>Claim <c>tenant_id</c> como Guid; <c>null</c> si falta o es inválido.</summary>
    Guid? TenantId { get; }

    /// <summary>Claim <c>cliente_id</c> como Guid; solo se expone cuando el rol es <see cref="SecunitecRoles.Cliente"/>.</summary>
    Guid? ClienteId { get; }

    /// <summary>Claim <c>client_id</c> (aplicación OAuth), si existe.</summary>
    string? ClientId { get; }

    /// <summary>Todos los valores del claim <c>role</c>.</summary>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>Indica si el llamador tiene el rol indicado (comparación exacta).</summary>
    bool IsInRole(string role);
}
