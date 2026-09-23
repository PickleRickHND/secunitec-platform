using Secunitec.Billing.Domain;
using Secunitec.BuildingBlocks.Security;

namespace Secunitec.Billing.Application;

/// <summary>
/// Autorización y auditoría comunes a los casos de uso (docs/PLAN.md §3.2 y etapa 1.3):
/// - cada caso de uso exige una política de <see cref="SecunitecPolicies"/>, con la misma matriz que la API;
/// - el tenant sale siempre del token, nunca del request (ABAC);
/// - todo acceso denegado queda auditado (A09, T-Repudiation).
/// </summary>
public sealed class ContextoSeguridad(ICurrentUser user, IAuditoria auditoria, IRequestContext request, TimeProvider time)
{
    /// <summary>Tenant del token; sin él no hay operación posible.</summary>
    public Guid Tenant => user.TenantId is Guid id && id != Guid.Empty
        ? id : throw new BillingAccessException("Falta un tenant válido en el token.");

    /// <summary>
    /// Autor de una escritura. Se resuelve antes de tocar la base: si el token no trae un sub GUID, la operación se
    /// rechaza sin dejar cambios a medias ni sin auditar.
    /// </summary>
    public Guid Actor => user.UserId is Guid id && id != Guid.Empty
        ? id : throw new BillingAccessException("Esta acción requiere un usuario.");

    /// <summary>
    /// Alcance de lectura del usuario: el cliente_id para el rol Cliente y "tenant" para los demás. Va en las claves
    /// de caché para que un Cliente nunca reciba el listado de otro.
    /// </summary>
    public string Alcance => EsSoloCliente ? "cliente:" + (user.ClienteId?.ToString("N") ?? "ninguno") : "tenant";

    public bool EsSoloCliente => user.IsInRole(SecunitecRoles.Cliente) &&
        !user.IsInRole(SecunitecRoles.Admin) && !user.IsInRole(SecunitecRoles.Facturador) && !user.IsInRole(SecunitecRoles.Auditor);

    public DateTimeOffset Ahora => time.GetUtcNow();

    /// <summary>Exige la política; si el usuario no la cumple, audita el intento y lanza 403.</summary>
    public async Task Exigir(string policy, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> roles = SecunitecPolicies.RolesByPolicy[policy];
        if (!user.IsAuthenticated || !roles.Any(user.IsInRole))
        {
            throw await Denegacion(null, $"Sin la política {policy}.", cancellationToken);
        }
    }

    /// <summary>
    /// Audita un acceso denegado (si hay tenant al que atribuirlo) y devuelve la excepción 403 para lanzarla:
    /// <c>throw await seguridad.Denegacion(...)</c>.
    /// </summary>
    public async Task<BillingAccessException> Denegacion(string? recurso, string detalle, CancellationToken cancellationToken)
    {
        if (user.TenantId is Guid tenant && tenant != Guid.Empty)
        {
            await auditoria.Registrar(Evento(tenant, AccionesAuditoria.AccesoDenegado, recurso, ResultadoAuditoria.Denegado, detalle),
                cancellationToken);
        }

        return new BillingAccessException("No tiene permiso para esta operación o este recurso.");
    }

    /// <summary>Registra una operación exitosa del usuario actual.</summary>
    public Task Registrar(string accion, Guid recurso, string? detalle, CancellationToken cancellationToken) =>
        auditoria.Registrar(Evento(Tenant, accion, recurso.ToString("D"), ResultadoAuditoria.Exito, detalle), cancellationToken);

    private EventoAuditoria Evento(Guid tenant, string accion, string? recurso, ResultadoAuditoria resultado, string? detalle) =>
        new(time.GetUtcNow(), tenant, user.UserId, accion, recurso, resultado, request.Ip, request.CorrelationId, detalle);
}
