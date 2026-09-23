using Secunitec.Billing.Domain;
using Secunitec.BuildingBlocks.Security;

namespace Secunitec.Billing.Application.Tests;

// Criterio de done de 1.3: flujo crear → emitir → anular; validaciones; Facturador emite y Auditor no; Cliente ve
// solo lo suyo; otro tenant recibe denegación y queda auditado.
public sealed class FacturasServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static NuevaFactura Nueva(Guid clienteId, decimal precio = 100) =>
        new(clienteId, [new NuevaLinea("Monitoreo mensual", 1, precio, false), new NuevaLinea("Instalación", 1, 50, true)]);

    [Fact]
    public async Task Facturador_CreaEmiteYAnula_ConTotalesDelDominioYAuditoria()
    {
        Escenario escenario = new();
        Guid tenant = escenario.TenantConObligado();
        Cliente cliente = escenario.Cliente(tenant, "Banco Atlántida");
        FakeUser facturador = new(tenant, SecunitecRoles.Facturador);
        FacturasService service = escenario.Facturas(facturador);

        FacturaDetalle creada = await service.Crear(Nueva(cliente.Id), Ct);
        FacturaDetalle emitida = await service.Emitir(creada.Id, Escenario.Hoy, Ct);
        FacturaDetalle anulada = await service.Anular(creada.Id, new AnularFactura("Error en el monto"), Ct);

        Assert.Equal(EstadoFactura.Borrador, creada.Estado);
        Assert.Equal(150.00m, creada.Subtotal);
        Assert.Equal(15.00m, creada.Isv);
        Assert.Equal(165.00m, creada.Total);
        Assert.Equal("Banco Atlántida", creada.ClienteNombre);
        Assert.Equal("000-001-01-00000001", emitida.Numero);
        Assert.Equal(EstadoFactura.Anulada, anulada.Estado);
        Assert.Equal("Error en el monto", anulada.MotivoAnulacion);
        Assert.Equal(
            [AccionesAuditoria.FacturaCreada, AccionesAuditoria.FacturaEmitida, AccionesAuditoria.FacturaAnulada],
            escenario.Auditoria.Eventos.Select(x => x.Accion));
        Assert.All(escenario.Auditoria.Eventos, evento =>
        {
            Assert.Equal(ResultadoAuditoria.Exito, evento.Resultado);
            Assert.Equal(facturador.Sub, evento.Actor);
            Assert.Equal("203.0.113.7", evento.Ip);
            Assert.Equal("prueba-1", evento.CorrelationId);
        });
    }

    [Fact]
    public async Task Crear_SinLineas_EsErrorDeValidacionSinEscribir()
    {
        Escenario escenario = new();
        Guid tenant = escenario.TenantConObligado();
        Cliente cliente = escenario.Cliente(tenant);

        BillingValidationException error = await Assert.ThrowsAsync<BillingValidationException>(() =>
            escenario.Facturas(new FakeUser(tenant, SecunitecRoles.Facturador)).Crear(new NuevaFactura(cliente.Id, []), Ct));

        Assert.Contains("lineas", error.Errors.Keys);
        Assert.Empty(escenario.Datos.Facturas);
        Assert.Equal(0, escenario.UnitOfWork.Transacciones);
    }

    [Fact]
    public async Task Crear_MontoNegativo_EsErrorDeValidacionPorCampo()
    {
        Escenario escenario = new();
        Guid tenant = escenario.TenantConObligado();
        Cliente cliente = escenario.Cliente(tenant);

        BillingValidationException error = await Assert.ThrowsAsync<BillingValidationException>(() =>
            escenario.Facturas(new FakeUser(tenant, SecunitecRoles.Facturador)).Crear(Nueva(cliente.Id, precio: -10), Ct));

        Assert.Contains("lineas[0].precioUnitario", error.Errors.Keys);
        Assert.Empty(escenario.Datos.Facturas);
    }

    [Fact]
    public async Task Anular_SinMotivo_EsErrorDeValidacion()
    {
        Escenario escenario = new();
        Guid tenant = escenario.TenantConObligado();
        Factura factura = escenario.Factura(tenant, escenario.Cliente(tenant).Id);

        await Assert.ThrowsAsync<BillingValidationException>(() =>
            escenario.Facturas(new FakeUser(tenant, SecunitecRoles.Facturador)).Anular(factura.Id, new AnularFactura(" "), Ct));
    }

    [Fact]
    public async Task Auditor_NoPuedeEmitir_YQuedaAuditado()
    {
        Escenario escenario = new();
        Guid tenant = escenario.TenantConObligado();
        Factura factura = escenario.Factura(tenant, escenario.Cliente(tenant).Id);

        await Assert.ThrowsAsync<BillingAccessException>(() =>
            escenario.Facturas(new FakeUser(tenant, SecunitecRoles.Auditor)).Emitir(factura.Id, Escenario.Hoy, Ct));

        Assert.Equal(EstadoFactura.Borrador, factura.Estado);
        EventoAuditoria evento = Assert.Single(escenario.Auditoria.Eventos);
        Assert.Equal(AccionesAuditoria.AccesoDenegado, evento.Accion);
        Assert.Equal(ResultadoAuditoria.Denegado, evento.Resultado);
    }

    [Fact]
    public async Task OtroTenant_Obtener_Recibe403YQuedaAuditado()
    {
        Escenario escenario = new();
        Guid tenantA = escenario.TenantConObligado();
        Guid tenantB = escenario.TenantConObligado();
        Factura deA = escenario.Factura(tenantA, escenario.Cliente(tenantA).Id);

        await Assert.ThrowsAsync<BillingAccessException>(() =>
            escenario.Facturas(new FakeUser(tenantB, SecunitecRoles.Admin)).Obtener(deA.Id, Ct));

        EventoAuditoria evento = Assert.Single(escenario.Auditoria.Eventos);
        Assert.Equal(AccionesAuditoria.AccesoDenegado, evento.Accion);
        Assert.Equal(tenantB, evento.TenantId);
        Assert.Equal(deA.Id.ToString("D"), evento.Recurso);
    }

    [Fact]
    public async Task OtroTenant_Emitir_Recibe403SinTocarLaFactura()
    {
        Escenario escenario = new();
        Guid tenantA = escenario.TenantConObligado();
        Guid tenantB = escenario.TenantConObligado();
        Factura deA = escenario.Factura(tenantA, escenario.Cliente(tenantA).Id);

        await Assert.ThrowsAsync<BillingAccessException>(() =>
            escenario.Facturas(new FakeUser(tenantB, SecunitecRoles.Facturador)).Emitir(deA.Id, Escenario.Hoy, Ct));

        Assert.Equal(EstadoFactura.Borrador, deA.Estado);
    }

    [Fact]
    public async Task FacturaInexistente_Recibe404SinAuditar()
    {
        Escenario escenario = new();
        Guid tenant = escenario.TenantConObligado();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            escenario.Facturas(new FakeUser(tenant, SecunitecRoles.Facturador)).Obtener(Guid.NewGuid(), Ct));

        Assert.Empty(escenario.Auditoria.Eventos);
    }

    [Fact]
    public async Task Cliente_SoloVeSusFacturas_YLaAjenaEsDenegada()
    {
        Escenario escenario = new();
        Guid tenant = escenario.TenantConObligado();
        Cliente propio = escenario.Cliente(tenant, "Propio");
        Cliente ajeno = escenario.Cliente(tenant, "Ajeno");
        Factura visible = escenario.Factura(tenant, propio.Id);
        Factura privada = escenario.Factura(tenant, ajeno.Id);
        FacturasService service = escenario.Facturas(new FakeUser(tenant, SecunitecRoles.Cliente, propio.Id));

        Pagina<FacturaResumen> listado = await service.Listar(new FiltroFacturas(), Ct);

        Assert.Equal(visible.Id, Assert.Single(listado.Items).Id);
        Assert.Equal(visible.Id, (await service.Obtener(visible.Id, Ct)).Id);
        await Assert.ThrowsAsync<BillingAccessException>(() => service.Obtener(privada.Id, Ct));
        await Assert.ThrowsAsync<BillingAccessException>(() => service.Crear(Nueva(propio.Id), Ct));
    }

    [Fact]
    public async Task Crear_ConClienteDeOtroTenant_Recibe403()
    {
        Escenario escenario = new();
        Guid tenantA = escenario.TenantConObligado();
        Guid tenantB = escenario.TenantConObligado();
        Cliente deA = escenario.Cliente(tenantA);

        await Assert.ThrowsAsync<BillingAccessException>(() =>
            escenario.Facturas(new FakeUser(tenantB, SecunitecRoles.Facturador)).Crear(Nueva(deA.Id), Ct));

        Assert.Empty(escenario.Datos.Facturas);
        Assert.Equal(AccionesAuditoria.AccesoDenegado, Assert.Single(escenario.Auditoria.Eventos).Accion);
    }

    [Fact]
    public async Task Crear_ClienteInexistente_EsReglaDeNegocio()
    {
        Escenario escenario = new();
        Guid tenant = escenario.TenantConObligado();

        BillingRuleException error = await Assert.ThrowsAsync<BillingRuleException>(() =>
            escenario.Facturas(new FakeUser(tenant, SecunitecRoles.Facturador)).Crear(Nueva(Guid.NewGuid()), Ct));

        Assert.Equal(BillingErrorCodes.ClienteAjeno, error.Code);
    }

    [Fact]
    public async Task Crear_SinObligadoRegistrado_EsReglaDeNegocio()
    {
        Escenario escenario = new();
        Guid tenant = Guid.NewGuid();
        Cliente cliente = escenario.Cliente(tenant);

        BillingRuleException error = await Assert.ThrowsAsync<BillingRuleException>(() =>
            escenario.Facturas(new FakeUser(tenant, SecunitecRoles.Facturador)).Crear(Nueva(cliente.Id), Ct));

        Assert.Equal(BillingErrorCodes.ObligadoRequerido, error.Code);
    }

    // Regresión: con un token sin sub GUID, Billing escribía y recién después fallaba al auditar (403 con el cambio hecho).
    [Fact]
    public async Task Emitir_TokenSinUsuario_RechazaSinEmitir()
    {
        Escenario escenario = new();
        Guid tenant = escenario.TenantConObligado();
        Factura borrador = escenario.Factura(tenant, escenario.Cliente(tenant).Id);

        await Assert.ThrowsAsync<BillingAccessException>(() =>
            escenario.Facturas(new FakeUser(tenant, SecunitecRoles.Facturador, withUser: false)).Emitir(borrador.Id, Escenario.Hoy, Ct));

        Assert.Equal(EstadoFactura.Borrador, borrador.Estado);
        Assert.Null(borrador.Numero);
    }

    [Fact]
    public async Task AgregarLinea_RecalculaYAudita()
    {
        Escenario escenario = new();
        Guid tenant = escenario.TenantConObligado();
        Factura factura = escenario.Factura(tenant, escenario.Cliente(tenant).Id);

        FacturaDetalle detalle = await escenario.Facturas(new FakeUser(tenant, SecunitecRoles.Facturador))
            .AgregarLinea(factura.Id, new NuevaLinea("Visita técnica", 2, 25, false), Ct);

        Assert.Equal(2, detalle.Lineas.Count);
        Assert.Equal(172.50m, detalle.Total);
        Assert.Equal(AccionesAuditoria.FacturaLineaAgregada, Assert.Single(escenario.Auditoria.Eventos).Accion);
    }

    [Fact]
    public async Task Listar_UsaLaCachePorAlcance_YSeInvalidaAlEscribir()
    {
        Escenario escenario = new();
        Guid tenant = escenario.TenantConObligado();
        Cliente cliente = escenario.Cliente(tenant);
        FacturasService service = escenario.Facturas(new FakeUser(tenant, SecunitecRoles.Facturador));

        Pagina<FacturaResumen> vacia = await service.Listar(new FiltroFacturas(), Ct);
        Assert.Single(escenario.Cache.Entradas);
        await service.Crear(Nueva(cliente.Id), Ct);
        Pagina<FacturaResumen> conUna = await service.Listar(new FiltroFacturas(), Ct);

        Assert.Equal(0, vacia.Total);
        Assert.Equal(1, conUna.Total);
    }

    [Fact]
    public async Task Listar_FiltroInvalido_EsErrorDeValidacion()
    {
        Escenario escenario = new();
        Guid tenant = escenario.TenantConObligado();
        FacturasService service = escenario.Facturas(new FakeUser(tenant, SecunitecRoles.Auditor));

        await Assert.ThrowsAsync<BillingValidationException>(() => service.Listar(new FiltroFacturas(Tamano: 500), Ct));
        await Assert.ThrowsAsync<BillingValidationException>(() => service.Listar(
            new FiltroFacturas(Desde: new DateOnly(2026, 9, 2), Hasta: new DateOnly(2026, 9, 1)), Ct));
    }
}
