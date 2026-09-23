namespace Secunitec.Billing.Domain;

public enum ResultadoAuditoria { Exito, Denegado, Fallido }

/// <summary>
/// Evento de auditoría de seguridad (docs/PLAN.md §3.1, R08, A09): solo se inserta; nunca se actualiza ni se borra
/// desde la aplicación. Infrastructure lo persiste en MongoDB.
/// </summary>
public sealed record EventoAuditoria(
    DateTimeOffset Timestamp,
    Guid TenantId,
    Guid? Actor,
    string Accion,
    string? Recurso,
    ResultadoAuditoria Resultado,
    string? Ip,
    string? CorrelationId,
    string? Detalle);

/// <summary>Acciones auditadas por Billing. Solo se agregan; nunca se renombran.</summary>
public static class AccionesAuditoria
{
    public const string FacturaCreada = "factura.creada";
    public const string FacturaLineaAgregada = "factura.linea_agregada";
    public const string FacturaLineaQuitada = "factura.linea_quitada";
    public const string FacturaEmitida = "factura.emitida";
    public const string FacturaAnulada = "factura.anulada";
    public const string ClienteCreado = "cliente.creado";
    public const string ClienteActualizado = "cliente.actualizado";
    public const string ObligadoCreado = "obligado.creado";
    public const string ObligadoCaiActualizado = "obligado.cai_actualizado";
    public const string ObligadoActivado = "obligado.activado";
    public const string ObligadoDesactivado = "obligado.desactivado";
    public const string AccesoDenegado = "acceso.denegado";
}
