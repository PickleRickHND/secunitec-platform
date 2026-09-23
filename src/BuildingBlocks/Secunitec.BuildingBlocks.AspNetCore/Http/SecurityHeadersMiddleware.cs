using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Secunitec.BuildingBlocks.AspNetCore.Http;

/// <summary>
/// Cabeceras de seguridad en toda respuesta y supresión de las que delatan tecnología.
/// R06 / OWASP A05: sin <c>X-Powered-By</c> (la de <c>Server</c> la quita Kestrel, ver <c>KestrelExtensions</c>).
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SecurityHeadersOptions _options;

    public SecurityHeadersMiddleware(RequestDelegate next, IOptions<SecurityHeadersOptions> options)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(options);
        _next = next;
        _options = options.Value;
    }

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.OnStarting(() =>
        {
            IHeaderDictionary headers = context.Response.Headers;

            // R06: nunca revelar el stack.
            headers.Remove("X-Powered-By");
            headers.Remove(HeaderNames.Server);
            headers.Remove("X-AspNet-Version");
            headers.Remove("X-AspNetMvc-Version");

            headers[HeaderNames.XContentTypeOptions] = "nosniff";
            headers[HeaderNames.XFrameOptions] = _options.FrameOptions;
            headers["Referrer-Policy"] = _options.ReferrerPolicy;
            headers["Permissions-Policy"] = _options.PermissionsPolicy;

            // Una CSP que ya trae la respuesta es más específica que la de este servicio: el gateway reenvía las
            // páginas de Identity con su propia CSP (estilos y fuentes 'self') y no debe pisarla con default-src 'none'.
            if (!string.IsNullOrWhiteSpace(_options.ContentSecurityPolicy) &&
                !headers.ContainsKey(HeaderNames.ContentSecurityPolicy))
            {
                headers[HeaderNames.ContentSecurityPolicy] = _options.ContentSecurityPolicy;
            }

            if (_options.NoStore && !headers.ContainsKey(HeaderNames.CacheControl))
            {
                headers[HeaderNames.CacheControl] = "no-store";
            }

            return Task.CompletedTask;
        });

        return _next(context);
    }
}

/// <summary>Registro del middleware de cabeceras.</summary>
public static class SecurityHeadersExtensions
{
    /// <summary>Configura <see cref="SecurityHeadersOptions"/>; opcional, los defaults sirven para APIs JSON.</summary>
    public static IServiceCollection AddSecunitecSecurityHeaders(
        this IServiceCollection services,
        Action<SecurityHeadersOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<SecurityHeadersOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        return services;
    }

    /// <summary>Agrega <see cref="SecurityHeadersMiddleware"/> al pipeline.</summary>
    public static IApplicationBuilder UseSecunitecSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
