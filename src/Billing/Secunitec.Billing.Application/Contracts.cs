using Secunitec.Billing.Domain;

namespace Secunitec.Billing.Application;

// Peticiones (lo que llega por HTTP). Nunca traen el tenant: sale siempre del token (docs/PLAN.md §3.2).

public sealed record NuevaLinea(string Descripcion, decimal Cantidad, decimal PrecioUnitario, bool Exento);

public sealed record NuevaFactura(Guid ClienteId, IReadOnlyList<NuevaLinea> Lineas);

public sealed record AnularFactura(string Motivo);

public sealed record DatosCliente(string Nombre, string? Rtn, string? Email);

public sealed record NuevoObligado(string Rtn, string RazonSocial, string Cai, string Prefijo,
    int RangoDesde, int RangoHasta, DateOnly FechaLimiteEmision);

public sealed record ActualizarCai(string Cai, string Prefijo, int RangoDesde, int RangoHasta, DateOnly FechaLimiteEmision);

public sealed record FiltroFacturas(
    EstadoFactura? Estado = null,
    Guid? ClienteId = null,
    DateOnly? Desde = null,
    DateOnly? Hasta = null,
    int Pagina = 1,
    int Tamano = 20);

public sealed record FiltroClientes(string? Buscar = null, int Pagina = 1, int Tamano = 20);

public sealed record FiltroAuditoria(
    string? Accion = null,
    ResultadoAuditoria? Resultado = null,
    DateOnly? Desde = null,
    DateOnly? Hasta = null,
    int Pagina = 1,
    int Tamano = 50);

// Respuestas (DTOs): el dominio nunca se serializa directo.

public sealed record Pagina<T>(IReadOnlyList<T> Items, int Total, int NumeroPagina, int Tamano);

public sealed record FacturaResumen(Guid Id, Guid ClienteId, string ClienteNombre, string? Numero, EstadoFactura Estado,
    DateOnly? FechaEmision, DateTimeOffset CreadaEn, decimal Subtotal, decimal Isv, decimal Total);

public sealed record LineaDto(int Id, string Descripcion, decimal Cantidad, decimal PrecioUnitario, bool Exento, decimal Importe);

public sealed record FacturaDetalle(Guid Id, Guid ClienteId, string ClienteNombre, string? ClienteRtn, string? Numero,
    string? Cai, EstadoFactura Estado, DateOnly? FechaEmision, DateTimeOffset CreadaEn, Guid CreadaPor,
    string? MotivoAnulacion, decimal Subtotal, decimal Isv, decimal Total, IReadOnlyList<LineaDto> Lineas);

public sealed record ClienteDto(Guid Id, string Nombre, string? Rtn, string? Email);

public sealed record ObligadoDto(Guid Id, string Rtn, string RazonSocial, string Cai, string Prefijo, int RangoDesde,
    int RangoHasta, int SiguienteCorrelativo, DateOnly FechaLimiteEmision, bool Activo);

/// <summary>Evento leído de la auditoría. <c>Origen</c>: <c>billing</c> o <c>identity</c>.</summary>
public sealed record EventoAuditoriaDto(string Id, DateTimeOffset Timestamp, string Origen, Guid? Actor, string Accion,
    string? Recurso, ResultadoAuditoria Resultado, string? Ip, string? CorrelationId, string? Detalle);

internal static class Mapeos
{
    public static ClienteDto ADto(this Cliente cliente) =>
        new(cliente.Id, cliente.Nombre, cliente.Rtn?.Value, cliente.Email);

    public static ObligadoDto ADto(this ObligadoTributario obligado) =>
        new(obligado.Id, obligado.Rtn.Value, obligado.RazonSocial, obligado.Cai.Value, obligado.Prefijo, obligado.RangoDesde,
            obligado.RangoHasta, obligado.SiguienteCorrelativo, obligado.FechaLimiteEmision, obligado.Activo);

    public static FacturaDetalle ADetalle(this Factura factura, Cliente? cliente) =>
        new(factura.Id, factura.ClienteId, cliente?.Nombre ?? "", cliente?.Rtn?.Value, factura.Numero, factura.Cai?.Value,
            factura.Estado, factura.FechaEmision, factura.CreadaEn, factura.CreadaPor, factura.MotivoAnulacion,
            factura.Subtotal.Amount, factura.Isv.Amount, factura.Total.Amount,
            factura.Lineas.Select(x => new LineaDto(x.Id, x.Descripcion, x.Cantidad, x.PrecioUnitario.Amount, x.Exento,
                x.Importe.Amount)).ToArray());
}
