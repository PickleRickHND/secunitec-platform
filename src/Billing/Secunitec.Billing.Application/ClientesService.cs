using Secunitec.Billing.Domain;
using Secunitec.BuildingBlocks.Security;

namespace Secunitec.Billing.Application;

/// <summary>Casos de uso de clientes: crear, actualizar y listar (Admin y Facturador).</summary>
public sealed class ClientesService(
    ContextoSeguridad seguridad,
    IClienteRepository clientes,
    IObligadoRepository obligados,
    IUnitOfWork unitOfWork,
    ICache cache)
{
    public async Task<ClienteDto> Crear(DatosCliente request, CancellationToken cancellationToken)
    {
        await seguridad.Exigir(SecunitecPolicies.ManageClientes, cancellationToken);
        Validacion.Exigir(Validacion.DatosCliente, request);
        _ = seguridad.Actor;
        if (await obligados.ObtenerActual(cancellationToken) is null)
        {
            throw new BillingRuleException(BillingErrorCodes.ObligadoRequerido, "Registre primero al obligado tributario.");
        }

        Cliente cliente = new(Guid.NewGuid(), seguridad.Tenant, request.Nombre, Rtn(request.Rtn), request.Email);
        await unitOfWork.EnTransaccion(_ =>
        {
            clientes.Agregar(cliente);
            return Task.FromResult(true);
        }, cancellationToken);

        await seguridad.Registrar(AccionesAuditoria.ClienteCreado, cliente.Id, null, cancellationToken);
        return cliente.ADto();
    }

    public async Task<ClienteDto> Actualizar(Guid id, DatosCliente request, CancellationToken cancellationToken)
    {
        await seguridad.Exigir(SecunitecPolicies.ManageClientes, cancellationToken);
        Validacion.Exigir(Validacion.DatosCliente, request);
        _ = seguridad.Actor;

        Cliente cliente = await unitOfWork.EnTransaccion(async ct =>
        {
            Cliente encontrado = await clientes.Obtener(id, ct) ?? throw await NoEncontrado(id, ct);
            encontrado.Actualizar(request.Nombre, Rtn(request.Rtn), request.Email);
            return encontrado;
        }, cancellationToken);

        // Los listados de facturas muestran el nombre del cliente.
        await cache.Invalidar(seguridad.Tenant, cancellationToken);
        await seguridad.Registrar(AccionesAuditoria.ClienteActualizado, cliente.Id, null, cancellationToken);
        return cliente.ADto();
    }

    public async Task<Pagina<ClienteDto>> Listar(FiltroClientes filtro, CancellationToken cancellationToken)
    {
        await seguridad.Exigir(SecunitecPolicies.ManageClientes, cancellationToken);
        Validacion.Exigir(Validacion.FiltroClientes, filtro);
        return await clientes.Listar(filtro, cancellationToken);
    }

    private static Rtn? Rtn(string? value) => string.IsNullOrWhiteSpace(value) ? null : new Rtn(value.Trim());

    private async Task<Exception> NoEncontrado(Guid id, CancellationToken cancellationToken) =>
        await clientes.ExisteFueraDeAlcance(id, cancellationToken)
            ? await seguridad.Denegacion(id.ToString("D"), "Cliente de otro tenant.", cancellationToken)
            : new KeyNotFoundException("El cliente no existe.");
}
