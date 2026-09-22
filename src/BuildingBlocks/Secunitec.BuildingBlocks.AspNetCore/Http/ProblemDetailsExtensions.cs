using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Secunitec.BuildingBlocks.AspNetCore.Http;

/// <summary>
/// Respuestas de error uniformes (RFC 9457) sin detalles internos. OWASP A05: en producción nunca se serializa
/// el mensaje ni el stack de una excepción; el <c>correlationId</c> permite cruzar con logs y auditoría.
/// </summary>
public static class ProblemDetailsExtensions
{
    /// <summary>Registra <c>ProblemDetails</c> con el <c>correlationId</c> como extensión.</summary>
    public static IServiceCollection AddSecunitecProblemDetails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            // A05: se elimina cualquier dato que un handler previo haya podido adjuntar sobre la excepción.
            context.ProblemDetails.Detail = context.HttpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError
                ? null
                : context.ProblemDetails.Detail;
            context.ProblemDetails.Extensions.Remove("exception");

            string? correlationId = context.HttpContext.GetCorrelationId();
            if (correlationId is not null)
            {
                context.ProblemDetails.Extensions["correlationId"] = correlationId;
            }
        });

        return services;
    }
}
