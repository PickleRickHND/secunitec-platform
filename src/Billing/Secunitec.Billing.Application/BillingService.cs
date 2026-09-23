using Secunitec.Billing.Domain;
using Secunitec.BuildingBlocks.Security;

namespace Secunitec.Billing.Application;

public sealed class BillingService(ICurrentUser currentUser, IBillingStore store, IBillingAudit audit, IInvoiceCache cache)
{
    private Guid Tenant => currentUser.TenantId is Guid id && id != Guid.Empty
        ? id : throw new BillingAccessException("Falta un tenant válido en el token.");

    private Guid Actor => currentUser.UserId is Guid id && id != Guid.Empty
        ? id : throw new BillingAccessException("Esta acción requiere un usuario.");

    private void Requerir(params string[] roles)
    {
        if (!currentUser.IsAuthenticated || !roles.Any(currentUser.IsInRole))
        {
            throw new BillingAccessException("No tiene permiso para esta operación.");
        }
    }

    private Guid? FiltroCliente()
    {
        Requerir(SecunitecRoles.Admin, SecunitecRoles.Facturador, SecunitecRoles.Auditor, SecunitecRoles.Cliente);
        if (!currentUser.IsInRole(SecunitecRoles.Cliente) || currentUser.IsInRole(SecunitecRoles.Admin) ||
            currentUser.IsInRole(SecunitecRoles.Facturador) || currentUser.IsInRole(SecunitecRoles.Auditor))
        {
            return null;
        }

        return currentUser.ClienteId is Guid id && id != Guid.Empty
            ? id : throw new BillingAccessException("Falta el cliente_id en el token.");
    }

    public async Task<ObligadoTributario> CrearObligado(NuevoObligado request, CancellationToken cancellationToken)
    {
        Requerir(SecunitecRoles.Admin);
        if (await store.ExisteObligado(Tenant, cancellationToken))
        {
            throw new BillingRuleException("El tenant ya tiene un obligado registrado.");
        }

        ObligadoTributario obligado = new(Tenant, request.Rtn, request.RazonSocial, request.Cai,
            request.Prefijo, request.RangoDesde, request.RangoHasta, request.FechaLimiteEmision);
        await store.AgregarObligado(obligado, cancellationToken);
        await audit.Registrar(Tenant, Actor, "obligado.creado", obligado.Id, cancellationToken);
        return obligado;
    }

    public async Task<Cliente> CrearCliente(NuevoCliente request, CancellationToken cancellationToken)
    {
        Requerir(SecunitecRoles.Admin, SecunitecRoles.Facturador);
        if (!await store.ExisteObligado(Tenant, cancellationToken))
        {
            throw new BillingRuleException("Registre primero al obligado tributario.");
        }

        Cliente cliente = new(Guid.NewGuid(), Tenant, request.Nombre, request.Rtn, request.Email);
        await store.AgregarCliente(cliente, cancellationToken);
        await audit.Registrar(Tenant, Actor, "cliente.creado", cliente.Id, cancellationToken);
        return cliente;
    }

    public async Task<Factura> CrearFactura(NuevaFactura request, CancellationToken cancellationToken)
    {
        Requerir(SecunitecRoles.Admin, SecunitecRoles.Facturador);
        if (request.Lineas is null || request.Lineas.Count == 0 || request.Lineas.Count > 100)
        {
            throw new BillingRuleException("La factura debe tener entre 1 y 100 líneas.");
        }
        if (await store.ObtenerCliente(Tenant, request.ClienteId, cancellationToken) is null)
        {
            throw new BillingRuleException("El cliente no pertenece a este obligado.");
        }

        Factura factura = new(Guid.NewGuid(), Tenant, request.ClienteId, Actor,
            request.Lineas.Select(x => new LineaFactura(x.Descripcion, x.Cantidad, x.PrecioUnitario, x.Exento)).ToArray());
        await store.AgregarFactura(factura, cancellationToken);
        await cache.Invalidar(Tenant, cancellationToken);
        await audit.Registrar(Tenant, Actor, "factura.creada", factura.Id, cancellationToken);
        return factura;
    }

    public async Task<Factura> Emitir(Guid id, DateOnly hoy, CancellationToken cancellationToken)
    {
        Requerir(SecunitecRoles.Admin, SecunitecRoles.Facturador);
        Factura factura = await store.Emitir(Tenant, id, hoy, cancellationToken)
            ?? throw new KeyNotFoundException("Factura no encontrada.");
        await cache.Invalidar(Tenant, cancellationToken);
        await audit.Registrar(Tenant, Actor, "factura.emitida", factura.Id, cancellationToken);
        return factura;
    }

    public async Task<Factura> Anular(Guid id, CancellationToken cancellationToken)
    {
        Requerir(SecunitecRoles.Admin, SecunitecRoles.Facturador);
        Factura factura = await store.Anular(Tenant, id, cancellationToken)
            ?? throw new KeyNotFoundException("Factura no encontrada.");
        await cache.Invalidar(Tenant, cancellationToken);
        await audit.Registrar(Tenant, Actor, "factura.anulada", factura.Id, cancellationToken);
        return factura;
    }

    public async Task<Factura> Obtener(Guid id, CancellationToken cancellationToken)
    {
        Guid? clienteId = FiltroCliente();
        return await store.ObtenerFactura(Tenant, id, clienteId, cancellationToken)
            ?? throw new KeyNotFoundException("Factura no encontrada.");
    }

    public async Task<IReadOnlyList<FacturaResumen>> Listar(CancellationToken cancellationToken)
    {
        Guid? clienteId = FiltroCliente();
        IReadOnlyList<FacturaResumen>? cached = await cache.ObtenerListado(Tenant, clienteId, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        IReadOnlyList<Factura> facturas = await store.ListarFacturas(Tenant, clienteId, cancellationToken);
        FacturaResumen[] resumen = facturas.Select(x => new FacturaResumen(x.Id, x.ClienteId,
            x.Numero, x.Estado, x.Subtotal, x.Isv, x.Total)).ToArray();
        await cache.GuardarListado(Tenant, clienteId, resumen, cancellationToken);
        return resumen;
    }
}
