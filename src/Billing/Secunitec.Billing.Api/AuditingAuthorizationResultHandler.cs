using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Secunitec.Billing.Application;
using Secunitec.Billing.Domain;
using Secunitec.BuildingBlocks.Security;

namespace Secunitec.Billing.Api;

/// <summary>
/// A09 / etapa 1.3: los 403 que decide la política del endpoint (antes de llegar al caso de uso) también quedan
/// auditados como <c>acceso.denegado</c>. Si la auditoría falla, el 403 se mantiene.
/// </summary>
public sealed partial class AuditingAuthorizationResultHandler(ILogger<AuditingAuthorizationResultHandler> logger)
    : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);
        if (authorizeResult.Forbidden)
        {
            try
            {
                ICurrentUser user = context.RequestServices.GetRequiredService<ICurrentUser>();
                if (user.TenantId is Guid tenant && tenant != Guid.Empty)
                {
                    IRequestContext request = context.RequestServices.GetRequiredService<IRequestContext>();
                    TimeProvider time = context.RequestServices.GetRequiredService<TimeProvider>();
                    await context.RequestServices.GetRequiredService<IAuditoria>().Registrar(new EventoAuditoria(
                        time.GetUtcNow(), tenant, user.UserId, AccionesAuditoria.AccesoDenegado,
                        $"{context.Request.Method} {context.Request.Path}", ResultadoAuditoria.Denegado,
                        request.Ip, request.CorrelationId, "Rol sin permiso para este endpoint."), context.RequestAborted);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AuditoriaFallida(logger, ex);
            }
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }

    [LoggerMessage(EventId = 2202, Level = LogLevel.Warning, Message = "No se pudo auditar un acceso denegado por política.")]
    private static partial void AuditoriaFallida(ILogger logger, Exception exception);
}
