using Secunitec.Billing.Domain;
using Secunitec.BuildingBlocks.Security;

namespace Secunitec.Billing.Application.Tests;

public sealed class ClientesYObligadoTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Cliente_CrearYActualizar_ValidaElRtnYAudita()
    {
        Escenario escenario = new();
        Guid tenant = escenario.TenantConObligado();
        ClientesService service = escenario.Clientes(new FakeUser(tenant, SecunitecRoles.Facturador));

        ClienteDto creado = await service.Crear(new DatosCliente("Ferretería El Tornillo", "08011999000001", "compras@tornillo.hn"), Ct);
        ClienteDto actualizado = await service.Actualizar(creado.Id, new DatosCliente("Ferretería El Tornillo S.A.", null, null), Ct);
        BillingValidationException invalido = await Assert.ThrowsAsync<BillingValidationException>(() =>
            service.Crear(new DatosCliente("Otro", "123", null), Ct));

        Assert.Equal("08011999000001", creado.Rtn);
        Assert.Equal("Ferretería El Tornillo S.A.", actualizado.Nombre);
        Assert.Null(actualizado.Rtn);
        Assert.Contains("rtn", invalido.Errors.Keys);
        Assert.Equal([AccionesAuditoria.ClienteCreado, AccionesAuditoria.ClienteActualizado],
            escenario.Auditoria.Eventos.Select(x => x.Accion));
    }

    [Fact]
    public async Task Cliente_ActualizarDeOtroTenant_Recibe403()
    {
        Escenario escenario = new();
        Guid tenantA = escenario.TenantConObligado();
        Guid tenantB = escenario.TenantConObligado();
        Cliente deA = escenario.Cliente(tenantA, "De A");

        await Assert.ThrowsAsync<BillingAccessException>(() => escenario.Clientes(new FakeUser(tenantB, SecunitecRoles.Admin))
            .Actualizar(deA.Id, new DatosCliente("Robado", null, null), Ct));

        Assert.Equal("De A", deA.Nombre);
    }

    [Fact]
    public async Task Cliente_Auditor_NoLista()
    {
        Escenario escenario = new();
        Guid tenant = escenario.TenantConObligado();

        await Assert.ThrowsAsync<BillingAccessException>(() =>
            escenario.Clientes(new FakeUser(tenant, SecunitecRoles.Auditor)).Listar(new FiltroClientes(), Ct));
    }

    [Fact]
    public async Task Obligado_CrearDosVeces_Conflicto()
    {
        Escenario escenario = new();
        Guid tenant = escenario.TenantConObligado();

        await Assert.ThrowsAsync<BillingConflictException>(() => escenario.Obligados(new FakeUser(tenant, SecunitecRoles.Admin))
            .Crear(new NuevoObligado("08011999654321", "Otra", "AAAAAA-BBBBBB-CCCCCC-DDDDDD-EEEEEE-FF", "000-001-01", 1, 10,
                new DateOnly(2026, 12, 31)), Ct));
        Assert.Single(escenario.Datos.Obligados);
    }

    [Fact]
    public async Task Obligado_AdminActualizaCaiYDesactiva()
    {
        Escenario escenario = new();
        Guid tenant = escenario.TenantConObligado();
        ObligadoService service = escenario.Obligados(new FakeUser(tenant, SecunitecRoles.Admin));

        ObligadoDto conCai = await service.ActualizarCai(
            new ActualizarCai("ZZZZZZ-YYYYYY-XXXXXX-WWWWWW-VVVVVV-UU", "000-002-01", 1, 500, new DateOnly(2027, 6, 30)), Ct);
        ObligadoDto inactivo = await service.Desactivar(Ct);

        Assert.Equal("ZZZZZZ-YYYYYY-XXXXXX-WWWWWW-VVVVVV-UU", conCai.Cai);
        Assert.Equal(500, conCai.RangoHasta);
        Assert.False(inactivo.Activo);
        Assert.Equal([AccionesAuditoria.ObligadoCaiActualizado, AccionesAuditoria.ObligadoDesactivado],
            escenario.Auditoria.Eventos.Select(x => x.Accion));
    }

    [Fact]
    public async Task Obligado_FacturadorNoCambiaElCai_YQuedaAuditado()
    {
        Escenario escenario = new();
        Guid tenant = escenario.TenantConObligado();

        await Assert.ThrowsAsync<BillingAccessException>(() => escenario.Obligados(new FakeUser(tenant, SecunitecRoles.Facturador))
            .ActualizarCai(new ActualizarCai("ZZZZZZ-YYYYYY-XXXXXX-WWWWWW-VVVVVV-UU", "000-002-01", 1, 500, new DateOnly(2027, 6, 30)), Ct));

        Assert.Equal(ResultadoAuditoria.Denegado, Assert.Single(escenario.Auditoria.Eventos).Resultado);
    }

    [Fact]
    public async Task Obligado_TodosLosRolesLoConsultan()
    {
        Escenario escenario = new();
        Guid tenant = escenario.TenantConObligado();

        ObligadoDto obligado = await escenario.Obligados(new FakeUser(tenant, SecunitecRoles.Cliente, Guid.NewGuid())).ObtenerActual(Ct);

        Assert.Equal("08011999123456", obligado.Rtn);
    }

    [Fact]
    public async Task Auditoria_AuditorConsulta_FacturadorNo()
    {
        Escenario escenario = new();
        Guid tenant = escenario.TenantConObligado();

        Pagina<EventoAuditoriaDto> pagina = await escenario.AuditoriaService(new FakeUser(tenant, SecunitecRoles.Auditor))
            .Consultar(new FiltroAuditoria(), Ct);
        await Assert.ThrowsAsync<BillingAccessException>(() =>
            escenario.AuditoriaService(new FakeUser(tenant, SecunitecRoles.Facturador)).Consultar(new FiltroAuditoria(), Ct));

        Assert.Empty(pagina.Items);
        Assert.Equal(AccionesAuditoria.AccesoDenegado, Assert.Single(escenario.Auditoria.Eventos).Accion);
    }
}
