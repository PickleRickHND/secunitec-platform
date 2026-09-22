using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Secunitec.BuildingBlocks.AspNetCore.Http;
using Secunitec.BuildingBlocks.AspNetCore.Security;

namespace Secunitec.BuildingBlocks.AspNetCore.Hosting;

/// <summary>Composición de los bloques comunes para no repetir el orden del pipeline en cada servicio.</summary>
public static class SecunitecWebApplicationExtensions
{
    /// <summary>
    /// Registra ProblemDetails, cabeceras de seguridad, <see cref="Secunitec.BuildingBlocks.Security.ICurrentUser"/>
    /// y las políticas de autorización (R04). No registra el esquema de autenticación: cada servicio agrega el suyo.
    /// </summary>
    public static IServiceCollection AddSecunitecDefaults(
        this IServiceCollection services,
        Action<SecurityHeadersOptions>? configureHeaders = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSecunitecProblemDetails();
        services.AddSecunitecSecurityHeaders(configureHeaders);
        services.AddSecunitecCurrentUser();
        services.AddSecunitecAuthorization();
        return services;
    }

    /// <summary>
    /// Pipeline base, en este orden: handler de excepciones (ProblemDetails) → status code pages → correlation id →
    /// cabeceras de seguridad. Debe llamarse antes de <c>UseAuthentication</c>/<c>UseAuthorization</c>.
    /// </summary>
    public static WebApplication UseSecunitecDefaults(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            // Los rechazos de Kestrel (413 body demasiado grande, 400 malformado) conservan su código real:
            // convertirlos en 500 falsearía la evidencia "429/4xx controlados, cero 500" de la Fase 3 (R18).
            StatusCodeSelector = ex => ex is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError,
        });
        app.UseStatusCodePages();
        app.UseSecunitecCorrelationId();
        app.UseSecunitecSecurityHeaders();
        return app;
    }
}
