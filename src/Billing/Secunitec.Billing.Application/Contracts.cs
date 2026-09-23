using Secunitec.Billing.Domain;

namespace Secunitec.Billing.Application;

public sealed record NuevaLinea(string Descripcion, decimal Cantidad, decimal PrecioUnitario, bool Exento);
public sealed record NuevaFactura(Guid ClienteId, IReadOnlyCollection<NuevaLinea> Lineas);
public sealed record NuevoCliente(string Nombre, string? Rtn, string? Email);
public sealed record NuevoObligado(string Rtn, string RazonSocial, string Cai, string Prefijo,
    int RangoDesde, int RangoHasta, DateOnly FechaLimiteEmision);
public sealed record FacturaResumen(Guid Id, Guid ClienteId, string? Numero, EstadoFactura Estado,
    decimal Subtotal, decimal Isv, decimal Total);

public interface IBillingStore
{
    Task<bool> ExisteObligado(Guid tenantId, CancellationToken cancellationToken);
    Task<Cliente?> ObtenerCliente(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task AgregarCliente(Cliente cliente, CancellationToken cancellationToken);
    Task AgregarObligado(ObligadoTributario obligado, CancellationToken cancellationToken);
    Task AgregarFactura(Factura factura, CancellationToken cancellationToken);
    Task<Factura?> ObtenerFactura(Guid tenantId, Guid id, Guid? clienteId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Factura>> ListarFacturas(Guid tenantId, Guid? clienteId, CancellationToken cancellationToken);
    Task<Factura?> Emitir(Guid tenantId, Guid id, DateOnly hoy, CancellationToken cancellationToken);
    Task<Factura?> Anular(Guid tenantId, Guid id, CancellationToken cancellationToken);
}

public interface IBillingAudit
{
    Task Registrar(Guid tenantId, Guid actor, string accion, Guid recurso, CancellationToken cancellationToken);
}

public interface IInvoiceCache
{
    Task<IReadOnlyList<FacturaResumen>?> ObtenerListado(Guid tenantId, Guid? clienteId, CancellationToken cancellationToken);
    Task GuardarListado(Guid tenantId, Guid? clienteId, IReadOnlyList<FacturaResumen> facturas, CancellationToken cancellationToken);
    Task Invalidar(Guid tenantId, CancellationToken cancellationToken);
}

public sealed class BillingAccessException(string message) : Exception(message);

/// <summary>El recurso ya existe (RTN repetido, obligado duplicado o alta concurrente); la API responde 409.</summary>
public sealed class BillingConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);
