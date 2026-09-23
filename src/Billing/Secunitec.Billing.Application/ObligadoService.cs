using Secunitec.Billing.Domain;
using Secunitec.BuildingBlocks.Security;

namespace Secunitec.Billing.Application;

/// <summary>Casos de uso del obligado del tenant: registrarlo, consultar, actualizar el CAI, activar y desactivar.</summary>
public sealed class ObligadoService(ContextoSeguridad seguridad, IObligadoRepository obligados, IUnitOfWork unitOfWork)
{
    public async Task<ObligadoDto> Crear(NuevoObligado request, CancellationToken cancellationToken)
    {
        await seguridad.Exigir(SecunitecPolicies.ManageObligados, cancellationToken);
        Validacion.Exigir(Validacion.NuevoObligado, request);
        _ = seguridad.Actor;
        if (await obligados.ObtenerActual(cancellationToken) is not null)
        {
            throw new BillingConflictException("Su empresa ya tiene un obligado registrado.");
        }

        ObligadoTributario obligado = new(seguridad.Tenant, new Rtn(request.Rtn), request.RazonSocial, new Cai(request.Cai),
            request.Prefijo, request.RangoDesde, request.RangoHasta, request.FechaLimiteEmision);
        await unitOfWork.EnTransaccion(_ =>
        {
            obligados.Agregar(obligado);
            return Task.FromResult(true);
        }, cancellationToken);

        await seguridad.Registrar(AccionesAuditoria.ObligadoCreado, obligado.Id, null, cancellationToken);
        return obligado.ADto();
    }

    public async Task<ObligadoDto> ObtenerActual(CancellationToken cancellationToken)
    {
        await seguridad.Exigir(SecunitecPolicies.ReadInvoices, cancellationToken);
        return (await obligados.ObtenerActual(cancellationToken) ?? throw SinObligado()).ADto();
    }

    public Task<ObligadoDto> ActualizarCai(ActualizarCai request, CancellationToken cancellationToken)
    {
        Validacion.Exigir(Validacion.ActualizarCai, request);
        return Modificar(AccionesAuditoria.ObligadoCaiActualizado, request.Cai,
            obligado => obligado.ActualizarCai(new Cai(request.Cai), request.Prefijo, request.RangoDesde, request.RangoHasta,
                request.FechaLimiteEmision), cancellationToken);
    }

    public Task<ObligadoDto> Activar(CancellationToken cancellationToken) =>
        Modificar(AccionesAuditoria.ObligadoActivado, null, obligado => obligado.Activar(), cancellationToken);

    public Task<ObligadoDto> Desactivar(CancellationToken cancellationToken) =>
        Modificar(AccionesAuditoria.ObligadoDesactivado, null, obligado => obligado.Desactivar(), cancellationToken);

    // El bloqueo de la fila serializa el cambio de CAI con las emisiones en curso (usan la misma fila).
    private async Task<ObligadoDto> Modificar(string accion, string? detalle, Action<ObligadoTributario> cambio,
        CancellationToken cancellationToken)
    {
        await seguridad.Exigir(SecunitecPolicies.ManageObligados, cancellationToken);
        _ = seguridad.Actor;

        ObligadoTributario obligado = await unitOfWork.EnTransaccion(async ct =>
        {
            ObligadoTributario bloqueado = await obligados.ObtenerActualParaActualizar(ct) ?? throw SinObligado();
            cambio(bloqueado);
            return bloqueado;
        }, cancellationToken);

        await seguridad.Registrar(accion, obligado.Id, detalle, cancellationToken);
        return obligado.ADto();
    }

    private static KeyNotFoundException SinObligado() => new("Su empresa aún no tiene un obligado tributario registrado.");
}
