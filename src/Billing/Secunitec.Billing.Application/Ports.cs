using Secunitec.Billing.Domain;

namespace Secunitec.Billing.Application;

// Puertos de la capa de aplicación (docs/PLAN.md, etapa 1.3). Infrastructure los implementa en 2.2.
// Los repositorios ya están acotados al tenant del token (filtro global) y, para el rol Cliente, a su cliente_id.

public interface IFacturaRepository
{
    /// <summary>Factura con sus líneas, solo si está al alcance del usuario.</summary>
    Task<Factura?> Obtener(Guid id, CancellationToken cancellationToken);

    /// <summary>Igual que <see cref="Obtener"/>, pero bloquea la fila hasta el fin de la transacción en curso.</summary>
    Task<Factura?> ObtenerParaActualizar(Guid id, CancellationToken cancellationToken);

    /// <summary>La factura existe pero es de otro tenant o de otro cliente (para responder 403 y auditar).</summary>
    Task<bool> ExisteFueraDeAlcance(Guid id, CancellationToken cancellationToken);

    Task<Pagina<FacturaResumen>> Listar(FiltroFacturas filtro, CancellationToken cancellationToken);

    void Agregar(Factura factura);
}

public interface IClienteRepository
{
    Task<Cliente?> Obtener(Guid id, CancellationToken cancellationToken);

    Task<bool> ExisteFueraDeAlcance(Guid id, CancellationToken cancellationToken);

    Task<Pagina<ClienteDto>> Listar(FiltroClientes filtro, CancellationToken cancellationToken);

    void Agregar(Cliente cliente);
}

public interface IObligadoRepository
{
    /// <summary>El obligado del tenant del token (1:1).</summary>
    Task<ObligadoTributario?> ObtenerActual(CancellationToken cancellationToken);

    /// <summary>Igual que <see cref="ObtenerActual"/>, pero bloquea la fila hasta el fin de la transacción.</summary>
    Task<ObligadoTributario?> ObtenerActualParaActualizar(CancellationToken cancellationToken);

    void Agregar(ObligadoTributario obligado);
}

/// <summary>Correlativo atómico: bloquea el rango del CAI del tenant dentro de la transacción en curso (R07).</summary>
public interface INumeradorFacturas
{
    Task<ObligadoTributario> BloquearRango(CancellationToken cancellationToken);
}

public interface IUnitOfWork
{
    /// <summary>
    /// Ejecuta la operación en una transacción, guarda los cambios y confirma. Un registro duplicado se traduce a
    /// <see cref="BillingConflictException"/>.
    /// </summary>
    Task<T> EnTransaccion<T>(Func<CancellationToken, Task<T>> operacion, CancellationToken cancellationToken);
}

public interface IAuditoria
{
    /// <summary>Inserta el evento. Nunca lanza: si el almacén falla, registra un warning (R18: sin 500).</summary>
    Task Registrar(EventoAuditoria evento, CancellationToken cancellationToken);

    Task<Pagina<EventoAuditoriaDto>> Consultar(Guid tenantId, FiltroAuditoria filtro, CancellationToken cancellationToken);
}

/// <summary>Caché de lecturas por tenant. Es una optimización: si falla, se sigue contra Postgres.</summary>
public interface ICache
{
    Task<T?> Obtener<T>(Guid tenantId, string clave, CancellationToken cancellationToken) where T : class;

    Task Guardar<T>(Guid tenantId, string clave, T valor, CancellationToken cancellationToken) where T : class;

    /// <summary>Descarta todas las entradas del tenant.</summary>
    Task Invalidar(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>Datos de la petición que la auditoría necesita y que no están en el token.</summary>
public interface IRequestContext
{
    string? Ip { get; }

    string? CorrelationId { get; }
}
