using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Secunitec.BuildingBlocks.AspNetCore.Hosting;

/// <summary>Hardening de Kestrel común a los tres servicios.</summary>
public static class KestrelExtensions
{
    /// <summary>Tamaño máximo de body por defecto (1 MiB): ninguna operación de facturación necesita más.</summary>
    public const long DefaultMaxRequestBodyBytes = 1024 * 1024;

    /// <summary>
    /// R06: sin cabecera <c>Server</c>. Además limita el body y acota timeouts para reducir la superficie de DoS
    /// (OWASP A05; complementa el rate limiting del gateway, R02).
    /// </summary>
    public static WebApplicationBuilder ConfigureSecunitecKestrel(
        this WebApplicationBuilder builder,
        long maxRequestBodyBytes = DefaultMaxRequestBodyBytes)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRequestBodyBytes);

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false; // R06
            kestrel.Limits.MaxRequestBodySize = maxRequestBodyBytes;
            kestrel.Limits.MaxRequestHeadersTotalSize = 32 * 1024;
            kestrel.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
            kestrel.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(60);
            kestrel.Limits.MinRequestBodyDataRate = new MinDataRate(bytesPerSecond: 240, gracePeriod: TimeSpan.FromSeconds(5));
        });

        return builder;
    }
}
