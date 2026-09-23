using Secunitec.Billing.Domain;
using Secunitec.BuildingBlocks.Security;

namespace Secunitec.Billing.Application;

/// <summary>Casos de uso de facturas: crear, cambiar líneas, emitir, anular, obtener y listar.</summary>
public sealed class FacturasService(
    ContextoSeguridad seguridad,
    IFacturaRepository facturas,
    IClienteRepository clientes,
    IObligadoRepository obligados,
    INumeradorFacturas numerador,
    IUnitOfWork unitOfWork,
    ICache cache,
    IMetricasFacturacion metricas)
{
    public async Task<FacturaDetalle> Crear(NuevaFactura request, CancellationToken cancellationToken)
    {
        await seguridad.Exigir(SecunitecPolicies.ManageInvoices, cancellationToken);
        Validacion.Exigir(Validacion.NuevaFactura, request);
        Guid actor = seguridad.Actor;

        Cliente cliente = await ClienteDelTenant(request.ClienteId, cancellationToken);
        if (await obligados.ObtenerActual(cancellationToken) is null)
        {
            throw new BillingRuleException(BillingErrorCodes.ObligadoRequerido, "Registre primero al obligado tributario.");
        }

        Factura factura = new(Guid.NewGuid(), seguridad.Tenant, cliente.Id, actor,
            request.Lineas.Select(x => new LineaFactura(x.Descripcion, x.Cantidad, x.PrecioUnitario, x.Exento)).ToArray(),
            seguridad.Ahora);
        await unitOfWork.EnTransaccion(_ =>
        {
            facturas.Agregar(factura);
            return Task.FromResult(true);
        }, cancellationToken);

        return await Cerrar(factura, AccionesAuditoria.FacturaCreada, null, cancellationToken);
    }

    public async Task<FacturaDetalle> AgregarLinea(Guid id, NuevaLinea request, CancellationToken cancellationToken)
    {
        await seguridad.Exigir(SecunitecPolicies.ManageInvoices, cancellationToken);
        Validacion.Exigir(Validacion.NuevaLinea, request);
        _ = seguridad.Actor;

        Factura factura = await unitOfWork.EnTransaccion(async ct =>
        {
            Factura bloqueada = await ParaActualizar(id, ct);
            bloqueada.AgregarLinea(new LineaFactura(request.Descripcion, request.Cantidad, request.PrecioUnitario, request.Exento));
            return bloqueada;
        }, cancellationToken);

        return await Cerrar(factura, AccionesAuditoria.FacturaLineaAgregada, null, cancellationToken);
    }

    public async Task<FacturaDetalle> QuitarLinea(Guid id, int lineaId, CancellationToken cancellationToken)
    {
        await seguridad.Exigir(SecunitecPolicies.ManageInvoices, cancellationToken);
        _ = seguridad.Actor;

        Factura factura = await unitOfWork.EnTransaccion(async ct =>
        {
            Factura bloqueada = await ParaActualizar(id, ct);
            bloqueada.QuitarLinea(lineaId);
            return bloqueada;
        }, cancellationToken);

        return await Cerrar(factura, AccionesAuditoria.FacturaLineaQuitada, $"Línea {lineaId}.", cancellationToken);
    }

    /// <summary>
    /// R07: bloquea la factura y luego el rango del CAI en la misma transacción, así dos emisiones concurrentes
    /// nunca reciben el mismo correlativo ni dejan huecos.
    /// </summary>
    public async Task<FacturaDetalle> Emitir(Guid id, DateOnly hoy, CancellationToken cancellationToken)
    {
        await seguridad.Exigir(SecunitecPolicies.ManageInvoices, cancellationToken);
        _ = seguridad.Actor;

        Factura factura = await unitOfWork.EnTransaccion(async ct =>
        {
            Factura bloqueada = await ParaActualizar(id, ct);
            ObligadoTributario rango = await numerador.BloquearRango(ct);
            bloqueada.Emitir(rango, hoy);
            return bloqueada;
        }, cancellationToken);

        metricas.FacturaEmitida();
        return await Cerrar(factura, AccionesAuditoria.FacturaEmitida, factura.Numero, cancellationToken);
    }

    public async Task<FacturaDetalle> Anular(Guid id, AnularFactura request, CancellationToken cancellationToken)
    {
        await seguridad.Exigir(SecunitecPolicies.ManageInvoices, cancellationToken);
        Validacion.Exigir(Validacion.AnularFactura, request);
        _ = seguridad.Actor;

        Factura factura = await unitOfWork.EnTransaccion(async ct =>
        {
            Factura bloqueada = await ParaActualizar(id, ct);
            bloqueada.Anular(request.Motivo);
            return bloqueada;
        }, cancellationToken);

        return await Cerrar(factura, AccionesAuditoria.FacturaAnulada, factura.MotivoAnulacion, cancellationToken);
    }

    public async Task<FacturaDetalle> Obtener(Guid id, CancellationToken cancellationToken)
    {
        await seguridad.Exigir(SecunitecPolicies.ReadInvoices, cancellationToken);
        Factura factura = await facturas.Obtener(id, cancellationToken) ?? throw await NoEncontrada(id, cancellationToken);
        return factura.ADetalle(await clientes.Obtener(factura.ClienteId, cancellationToken));
    }

    public async Task<Pagina<FacturaResumen>> Listar(FiltroFacturas filtro, CancellationToken cancellationToken)
    {
        await seguridad.Exigir(SecunitecPolicies.ReadInvoices, cancellationToken);
        Validacion.Exigir(Validacion.FiltroFacturas, filtro);

        string clave = $"facturas:{seguridad.Alcance}:{filtro.Estado}:{filtro.ClienteId:N}:{filtro.Desde:O}:{filtro.Hasta:O}:{filtro.Pagina}:{filtro.Tamano}";
        if (await cache.Obtener<Pagina<FacturaResumen>>(seguridad.Tenant, clave, cancellationToken) is { } cached)
        {
            return cached;
        }

        Pagina<FacturaResumen> pagina = await facturas.Listar(filtro, cancellationToken);
        await cache.Guardar(seguridad.Tenant, clave, pagina, cancellationToken);
        return pagina;
    }

    private async Task<Cliente> ClienteDelTenant(Guid clienteId, CancellationToken cancellationToken)
    {
        if (await clientes.Obtener(clienteId, cancellationToken) is { } cliente)
        {
            return cliente;
        }

        if (await clientes.ExisteFueraDeAlcance(clienteId, cancellationToken))
        {
            throw await seguridad.Denegacion(clienteId.ToString("D"), "Cliente de otro tenant.", cancellationToken);
        }

        throw new BillingRuleException(BillingErrorCodes.ClienteAjeno, "El cliente no existe en su empresa.");
    }

    private async Task<Factura> ParaActualizar(Guid id, CancellationToken cancellationToken) =>
        await facturas.ObtenerParaActualizar(id, cancellationToken) ?? throw await NoEncontrada(id, cancellationToken);

    // Otro tenant (o, para el rol Cliente, otro cliente) → 403 auditado; inexistente → 404 (docs/PLAN.md H3).
    private async Task<Exception> NoEncontrada(Guid id, CancellationToken cancellationToken) =>
        await facturas.ExisteFueraDeAlcance(id, cancellationToken)
            ? await seguridad.Denegacion(id.ToString("D"), "Factura fuera de su alcance.", cancellationToken)
            : new KeyNotFoundException("La factura no existe.");

    private async Task<FacturaDetalle> Cerrar(Factura factura, string accion, string? detalle, CancellationToken cancellationToken)
    {
        await cache.Invalidar(seguridad.Tenant, cancellationToken);
        await seguridad.Registrar(accion, factura.Id, detalle, cancellationToken);
        return factura.ADetalle(await clientes.Obtener(factura.ClienteId, cancellationToken));
    }
}
