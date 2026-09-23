using Secunitec.BuildingBlocks.Security;

namespace Secunitec.Billing.Application;

/// <summary>Consulta de la auditoría del tenant (Admin y Auditor).</summary>
public sealed class AuditoriaService(ContextoSeguridad seguridad, IAuditoria auditoria)
{
    public async Task<Pagina<EventoAuditoriaDto>> Consultar(FiltroAuditoria filtro, CancellationToken cancellationToken)
    {
        await seguridad.Exigir(SecunitecPolicies.ReadAudit, cancellationToken);
        Validacion.Exigir(Validacion.FiltroAuditoria, filtro);
        return await auditoria.Consultar(seguridad.Tenant, filtro, cancellationToken);
    }
}
