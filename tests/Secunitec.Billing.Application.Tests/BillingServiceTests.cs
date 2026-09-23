using Secunitec.Billing.Domain;
using Secunitec.BuildingBlocks.Security;

namespace Secunitec.Billing.Application.Tests;

public sealed class BillingServiceTests
{
    [Fact]
    public async Task CrearFactura_ClienteDeOtroTenant_Rechazado()
    {
        FakeStore store = new();
        Guid tenant = Guid.NewGuid();
        store.Obligados[tenant] = CrearObligado(tenant);
        Cliente ajeno = new(Guid.NewGuid(), Guid.NewGuid(), "Ajeno", null, null);
        store.Clientes[ajeno.Id] = ajeno;
        BillingService service = Servicio(store, new FakeUser(tenant, SecunitecRoles.Facturador));

        await Assert.ThrowsAsync<BillingRuleException>(() => service.CrearFactura(
            new NuevaFactura(ajeno.Id, [new NuevaLinea("servicio", 1, 100, false)]), CancellationToken.None));
        Assert.Empty(store.Facturas);
    }

    [Fact]
    public async Task Cliente_AlListar_YConsultar_SoloVeSusFacturas()
    {
        FakeStore store = new();
        Guid tenant = Guid.NewGuid();
        Guid propio = Guid.NewGuid();
        Guid ajeno = Guid.NewGuid();
        Factura privada = new(Guid.NewGuid(), tenant, ajeno, Guid.NewGuid(), [new LineaFactura("privada", 1, 1, false)]);
        Factura visible = new(Guid.NewGuid(), tenant, propio, Guid.NewGuid(), [new LineaFactura("visible", 1, 1, false)]);
        store.Facturas.AddRange([privada, visible]);
        BillingService service = Servicio(store, new FakeUser(tenant, SecunitecRoles.Cliente, propio));

        IReadOnlyList<FacturaResumen> listado = await service.Listar(CancellationToken.None);
        Assert.Single(listado);
        Assert.Equal(visible.Id, listado[0].Id);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.Obtener(privada.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Auditor_NoPuedeCrearNiEmitir()
    {
        FakeStore store = new();
        BillingService service = Servicio(store, new FakeUser(Guid.NewGuid(), SecunitecRoles.Auditor));
        await Assert.ThrowsAsync<BillingAccessException>(() => service.CrearCliente(
            new NuevoCliente("x", null, null), CancellationToken.None));
        await Assert.ThrowsAsync<BillingAccessException>(() => service.Emitir(Guid.NewGuid(), new DateOnly(2026, 9, 22), CancellationToken.None));
    }

    [Fact]
    public async Task CrearObligado_TenantYaRegistrado_Conflicto()
    {
        FakeStore store = new();
        Guid tenant = Guid.NewGuid();
        store.Obligados[tenant] = CrearObligado(tenant);
        BillingService service = Servicio(store, new FakeUser(tenant, SecunitecRoles.Admin));

        await Assert.ThrowsAsync<BillingConflictException>(() => service.CrearObligado(new NuevoObligado(
            "08011999654321", "Otra", "AAAAAA-BBBBBB-CCCCCC-DDDDDD-EEEEEE-FF", "000-001-01", 1, 10,
            new DateOnly(2026, 12, 31)), TestContext.Current.CancellationToken));
        Assert.Single(store.Obligados);
    }

    // Regresión: con un token sin sub GUID (p. ej. client credentials con sub = client_id), Billing guardaba el
    // cliente o emitía la factura y recién después fallaba al auditar: respondía 403 con el cambio ya hecho.
    [Fact]
    public async Task CrearCliente_TokenSinUsuario_RechazaSinEscribir()
    {
        FakeStore store = new();
        Guid tenant = Guid.NewGuid();
        store.Obligados[tenant] = CrearObligado(tenant);
        BillingService service = Servicio(store, new FakeUser(tenant, SecunitecRoles.Facturador, withUser: false));

        await Assert.ThrowsAsync<BillingAccessException>(() => service.CrearCliente(
            new NuevoCliente("Cliente", null, null), TestContext.Current.CancellationToken));
        Assert.Empty(store.Clientes);
    }

    [Fact]
    public async Task Emitir_TokenSinUsuario_RechazaSinEmitir()
    {
        FakeStore store = new();
        Guid tenant = Guid.NewGuid();
        store.Obligados[tenant] = CrearObligado(tenant);
        Factura borrador = new(Guid.NewGuid(), tenant, Guid.NewGuid(), Guid.NewGuid(), [new LineaFactura("x", 1, 1, false)]);
        store.Facturas.Add(borrador);
        BillingService service = Servicio(store, new FakeUser(tenant, SecunitecRoles.Facturador, withUser: false));

        await Assert.ThrowsAsync<BillingAccessException>(() => service.Emitir(
            borrador.Id, new DateOnly(2026, 9, 22), TestContext.Current.CancellationToken));
        Assert.Equal(EstadoFactura.Borrador, borrador.Estado);
        Assert.Null(borrador.Numero);
    }

    private static ObligadoTributario CrearObligado(Guid id) => new(id, "08011999123456", "Empresa",
        "AAAAAA-BBBBBB-CCCCCC-DDDDDD-EEEEEE-FF", "000-001-01", 1, 10, new DateOnly(2026, 12, 31));

    private static BillingService Servicio(FakeStore store, FakeUser user) =>
        new(user, store, new FakeAudit(), new FakeCache());

    private sealed class FakeUser(Guid tenantId, string assignedRole, Guid? clienteId = null, bool withUser = true) : ICurrentUser
    {
        private readonly Guid? _userId = withUser ? Guid.NewGuid() : null;

        public bool IsAuthenticated => true;
        public Guid? UserId => _userId;
        public Guid? TenantId => tenantId;
        public Guid? ClienteId => clienteId;
        public string? ClientId => null;
        public IReadOnlyCollection<string> Roles => [assignedRole];
        public bool IsInRole(string role) => role == assignedRole;
    }

    private sealed class FakeAudit : IBillingAudit
    {
        public Task Registrar(Guid tenantId, Guid actor, string accion, Guid recurso, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeCache : IInvoiceCache
    {
        public Task<IReadOnlyList<FacturaResumen>?> ObtenerListado(Guid tenantId, Guid? clienteId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FacturaResumen>?>(null);
        public Task GuardarListado(Guid tenantId, Guid? clienteId, IReadOnlyList<FacturaResumen> facturas, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task Invalidar(Guid tenantId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeStore : IBillingStore
    {
        public Dictionary<Guid, ObligadoTributario> Obligados { get; } = [];
        public Dictionary<Guid, Cliente> Clientes { get; } = [];
        public List<Factura> Facturas { get; } = [];

        public Task<bool> ExisteObligado(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(Obligados.ContainsKey(tenantId));
        public Task<Cliente?> ObtenerCliente(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Clientes.GetValueOrDefault(id) is { } c && c.ObligadoId == tenantId ? c : null);
        public Task AgregarCliente(Cliente cliente, CancellationToken cancellationToken) { Clientes.Add(cliente.Id, cliente); return Task.CompletedTask; }
        public Task AgregarObligado(ObligadoTributario obligado, CancellationToken cancellationToken) { Obligados.Add(obligado.Id, obligado); return Task.CompletedTask; }
        public Task AgregarFactura(Factura factura, CancellationToken cancellationToken) { Facturas.Add(factura); return Task.CompletedTask; }
        public Task<Factura?> ObtenerFactura(Guid tenantId, Guid id, Guid? clienteId, CancellationToken cancellationToken) =>
            Task.FromResult(Facturas.FirstOrDefault(x => x.ObligadoId == tenantId && x.Id == id &&
                (clienteId is null || x.ClienteId == clienteId)));
        public Task<IReadOnlyList<Factura>> ListarFacturas(Guid tenantId, Guid? clienteId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Factura>>(Facturas.Where(x => x.ObligadoId == tenantId &&
                (clienteId is null || x.ClienteId == clienteId)).ToArray());
        public Task<Factura?> Emitir(Guid tenantId, Guid id, DateOnly hoy, CancellationToken cancellationToken)
        {
            Factura? factura = Facturas.FirstOrDefault(x => x.ObligadoId == tenantId && x.Id == id);
            if (factura is not null)
            {
                factura.Emitir(Obligados[tenantId], hoy);
            }
            return Task.FromResult(factura);
        }
        public Task<Factura?> Anular(Guid tenantId, Guid id, CancellationToken cancellationToken)
        {
            Factura? factura = Facturas.FirstOrDefault(x => x.ObligadoId == tenantId && x.Id == id);
            factura?.Anular();
            return Task.FromResult(factura);
        }
    }
}
