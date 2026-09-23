// R09: el SPA (http://localhost:3000) y el gateway (https://localhost:8080) son orígenes distintos. La política solo
// admite los orígenes configurados, sin credenciales (el SPA manda Bearer, no cookies) y expone Retry-After para que
// el panel de resiliencia muestre la cuenta regresiva de los 429 (R10).

namespace Secunitec.Gateway;

public static class GatewayCors
{
    /// <summary>Nombre de la política que las rutas de YARP asignan con <c>CorsPolicy</c>.</summary>
    public const string SpaPolicy = "spa";

    public static IServiceCollection AddSecunitecCors(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Sin barra final: el navegador manda el Origin sin ella y ASP.NET compara el texto exacto. Una lista vacía
        // no admite ningún origen.
        string[] origins = (configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.Trim().TrimEnd('/'))
            .ToArray();

        services.AddCors(options => options.AddPolicy(SpaPolicy, policy => policy
            .WithOrigins(origins)
            .WithMethods(HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Delete)
            .WithHeaders("Authorization", "Content-Type", "X-Correlation-Id")
            .WithExposedHeaders("Retry-After", "X-Correlation-Id")
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10))));

        return services;
    }
}
