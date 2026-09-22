using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Secunitec.BuildingBlocks.Http;

namespace Secunitec.BuildingBlocks.AspNetCore.Http;

/// <summary>
/// Garantiza un <c>X-Correlation-Id</c> por petición: respeta el recibido si es válido, genera uno si falta o es
/// inválido, lo expone en <see cref="HttpContext.Items"/>, lo devuelve en la respuesta y lo agrega al scope de log.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    /// <summary>Clave en <see cref="HttpContext.Items"/>.</summary>
    public const string ItemKey = "Secunitec.CorrelationId";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(logger);
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string? incoming = context.Request.Headers[CorrelationId.HeaderName].FirstOrDefault();
        string correlationId = CorrelationId.IsValid(incoming) ? incoming! : CorrelationId.NewId();

        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationId.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using IDisposable? scope = _logger.BeginScope(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["CorrelationId"] = correlationId,
        });

        await _next(context).ConfigureAwait(false);
    }
}

/// <summary>Acceso al correlation id de la petición.</summary>
public static class CorrelationIdExtensions
{
    /// <summary>Agrega <see cref="CorrelationIdMiddleware"/> al pipeline.</summary>
    public static IApplicationBuilder UseSecunitecCorrelationId(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<CorrelationIdMiddleware>();
    }

    /// <summary>Correlation id asignado por el middleware, o <c>null</c> si el middleware no corrió.</summary>
    public static string? GetCorrelationId(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out object? value) ? value as string : null;
    }
}
