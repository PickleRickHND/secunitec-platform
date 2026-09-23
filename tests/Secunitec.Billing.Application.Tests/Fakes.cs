using Secunitec.Billing.Domain;
using Secunitec.BuildingBlocks.Security;

namespace Secunitec.Billing.Application.Tests;

// Dobles en memoria de los puertos (etapa 1.3). Los repositorios aplican el mismo alcance que el filtro global de EF:
// tenant del token y, para el rol Cliente, su cliente_id.

internal sealed class FakeUser(Guid tenantId, string role, Guid? clienteId = null, bool withUser = true) : ICurrentUser
{
    public Guid? Sub { get; } = withUser ? Guid.NewGuid() : null;
    public bool IsAuthenticated => true;
    public Guid? UserId => Sub;
    public Guid? TenantId => tenantId;
    public Guid? ClienteId => clienteId;
    public string? ClientId => null;
    public IReadOnlyCollection<string> Roles => [role];
    public bool IsInRole(string value) => value == role;
}

internal sealed class Datos
{
    public List<ObligadoTributario> Obligados { get; } = [];
    public List<Cliente> Clientes { get; } = [];
    public List<Factura> Facturas { get; } = [];
}

internal sealed class FakeFacturas(Datos datos, ICurrentUser user) : IFacturaRepository
{
    private IEnumerable<Factura> Visibles => datos.Facturas.Where(x => x.ObligadoId == user.TenantId &&
        (!Alcance.SoloCliente(user) || x.ClienteId == user.ClienteId));

    public Task<Factura?> Obtener(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Visibles.FirstOrDefault(x => x.Id == id));

    public Task<Factura?> ObtenerParaActualizar(Guid id, CancellationToken cancellationToken) => Obtener(id, cancellationToken);

    public Task<bool> ExisteFueraDeAlcance(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(datos.Facturas.Any(x => x.Id == id));

    public Task<Pagina<FacturaResumen>> Listar(FiltroFacturas filtro, CancellationToken cancellationToken)
    {
        Factura[] filtradas = Visibles.Where(x => filtro.Estado is null || x.Estado == filtro.Estado).ToArray();
        return Task.FromResult(new Pagina<FacturaResumen>(filtradas
            .Skip((filtro.Pagina - 1) * filtro.Tamano).Take(filtro.Tamano)
            .Select(x => new FacturaResumen(x.Id, x.ClienteId, datos.Clientes.FirstOrDefault(c => c.Id == x.ClienteId)?.Nombre ?? "",
                x.Numero, x.Estado, x.FechaEmision, x.CreadaEn, x.Subtotal.Amount, x.Isv.Amount, x.Total.Amount))
            .ToArray(), filtradas.Length, filtro.Pagina, filtro.Tamano));
    }

    public void Agregar(Factura factura) => datos.Facturas.Add(factura);
}

internal sealed class FakeClientes(Datos datos, ICurrentUser user) : IClienteRepository
{
    public Task<Cliente?> Obtener(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(datos.Clientes.FirstOrDefault(x => x.Id == id && x.ObligadoId == user.TenantId));

    public Task<bool> ExisteFueraDeAlcance(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(datos.Clientes.Any(x => x.Id == id));

    public Task<Pagina<ClienteDto>> Listar(FiltroClientes filtro, CancellationToken cancellationToken)
    {
        ClienteDto[] items = datos.Clientes.Where(x => x.ObligadoId == user.TenantId)
            .Select(x => new ClienteDto(x.Id, x.Nombre, x.Rtn?.Value, x.Email)).ToArray();
        return Task.FromResult(new Pagina<ClienteDto>(items, items.Length, filtro.Pagina, filtro.Tamano));
    }

    public void Agregar(Cliente cliente) => datos.Clientes.Add(cliente);
}

internal sealed class FakeObligados(Datos datos, ICurrentUser user) : IObligadoRepository, INumeradorFacturas
{
    public Task<ObligadoTributario?> ObtenerActual(CancellationToken cancellationToken) =>
        Task.FromResult(datos.Obligados.FirstOrDefault(x => x.Id == user.TenantId));

    public Task<ObligadoTributario?> ObtenerActualParaActualizar(CancellationToken cancellationToken) => ObtenerActual(cancellationToken);

    public async Task<ObligadoTributario> BloquearRango(CancellationToken cancellationToken) =>
        await ObtenerActual(cancellationToken) ?? throw new InvalidOperationException("Sin obligado.");

    public void Agregar(ObligadoTributario obligado) => datos.Obligados.Add(obligado);
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int Transacciones { get; private set; }

    public async Task<T> EnTransaccion<T>(Func<CancellationToken, Task<T>> operacion, CancellationToken cancellationToken)
    {
        T resultado = await operacion(cancellationToken);
        Transacciones++;
        return resultado;
    }
}

internal sealed class FakeAuditoria : IAuditoria
{
    public List<EventoAuditoria> Eventos { get; } = [];

    public Task Registrar(EventoAuditoria evento, CancellationToken cancellationToken)
    {
        Eventos.Add(evento);
        return Task.CompletedTask;
    }

    public Task<Pagina<EventoAuditoriaDto>> Consultar(Guid tenantId, FiltroAuditoria filtro, CancellationToken cancellationToken) =>
        Task.FromResult(new Pagina<EventoAuditoriaDto>(Eventos.Where(x => x.TenantId == tenantId)
            .Select(x => new EventoAuditoriaDto(Guid.NewGuid().ToString(), x.Timestamp, "billing", x.Actor, x.Accion, x.Recurso,
                x.Resultado, x.Ip, x.CorrelationId, x.Detalle)).ToArray(), Eventos.Count, filtro.Pagina, filtro.Tamano));
}

internal sealed class FakeCache : ICache
{
    public Dictionary<string, object> Entradas { get; } = [];

    public Task<T?> Obtener<T>(Guid tenantId, string clave, CancellationToken cancellationToken) where T : class =>
        Task.FromResult(Entradas.GetValueOrDefault($"{tenantId}:{clave}") as T);

    public Task Guardar<T>(Guid tenantId, string clave, T valor, CancellationToken cancellationToken) where T : class
    {
        Entradas[$"{tenantId}:{clave}"] = valor;
        return Task.CompletedTask;
    }

    public Task Invalidar(Guid tenantId, CancellationToken cancellationToken)
    {
        foreach (string clave in Entradas.Keys.Where(x => x.StartsWith(tenantId.ToString(), StringComparison.Ordinal)).ToArray())
        {
            Entradas.Remove(clave);
        }

        return Task.CompletedTask;
    }
}

internal sealed class FakeMetricas : IMetricasFacturacion
{
    public int Emitidas { get; private set; }

    public void FacturaEmitida() => Emitidas++;
}

internal sealed class FakeRequest : IRequestContext
{
    public string? Ip => "203.0.113.7";
    public string? CorrelationId => "prueba-1";
}

internal static class Alcance
{
    public static bool SoloCliente(ICurrentUser user) => user.IsInRole(SecunitecRoles.Cliente);
}

/// <summary>Arma los servicios con los dobles para un usuario.</summary>
internal sealed class Escenario
{
    public static readonly DateOnly Hoy = new(2026, 9, 22);

    public Escenario(Datos? datos = null)
    {
        Datos = datos ?? new Datos();
    }

    public Datos Datos { get; }
    public FakeAuditoria Auditoria { get; } = new();
    public FakeCache Cache { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public FakeMetricas Metricas { get; } = new();

    public static ObligadoTributario Obligado(Guid tenant) => new(tenant, new Rtn("08011999123456"), "Empresa",
        new Cai("AAAAAA-BBBBBB-CCCCCC-DDDDDD-EEEEEE-FF"), "000-001-01", 1, 10, new DateOnly(2026, 12, 31));

    public Guid TenantConObligado()
    {
        Guid tenant = Guid.NewGuid();
        Datos.Obligados.Add(Obligado(tenant));
        return tenant;
    }

    public Cliente Cliente(Guid tenant, string nombre = "Cliente")
    {
        Cliente cliente = new(Guid.NewGuid(), tenant, nombre, null, null);
        Datos.Clientes.Add(cliente);
        return cliente;
    }

    public Factura Factura(Guid tenant, Guid clienteId)
    {
        Factura factura = new(Guid.NewGuid(), tenant, clienteId, Guid.NewGuid(), [new LineaFactura("servicio", 1, 100, false)],
            DateTimeOffset.UtcNow);
        Datos.Facturas.Add(factura);
        return factura;
    }

    public ContextoSeguridad Seguridad(ICurrentUser user) => new(user, Auditoria, new FakeRequest(), TimeProvider.System);

    public FacturasService Facturas(ICurrentUser user) => new(Seguridad(user), new FakeFacturas(Datos, user),
        new FakeClientes(Datos, user), new FakeObligados(Datos, user), new FakeObligados(Datos, user), UnitOfWork, Cache, Metricas);

    public ClientesService Clientes(ICurrentUser user) =>
        new(Seguridad(user), new FakeClientes(Datos, user), new FakeObligados(Datos, user), UnitOfWork, Cache);

    public ObligadoService Obligados(ICurrentUser user) => new(Seguridad(user), new FakeObligados(Datos, user), UnitOfWork);

    public AuditoriaService AuditoriaService(ICurrentUser user) => new(Seguridad(user), Auditoria);
}
