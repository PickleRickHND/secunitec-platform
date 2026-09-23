namespace Secunitec.BuildingBlocks.AspNetCore.Security;

/// <summary>
/// Reescribe las URLs públicas de Identity hacia su dirección interna, solo en el backchannel con el que JwtBearer
/// descarga la metadata OIDC y el JWKS.
/// </summary>
/// <remarks>
/// Identity emite los tokens con el issuer público (la URL del gateway que ve el navegador), y su discovery anuncia
/// endpoints públicos. Dentro de Docker esa URL no apunta a Identity, así que Billing y el Gateway validan el
/// <c>iss</c> público pero descargan las llaves por la red interna (TB1). La petición reescrita lleva
/// <c>X-Forwarded-Host</c> y <c>X-Forwarded-Proto</c> públicos, como las que llegan por el gateway, para que
/// Identity siga anunciando URLs públicas (https) y el JWKS pase la validación de HTTPS de JwtBearer.
/// </remarks>
public sealed class InternalAuthorityHandler : DelegatingHandler
{
    private readonly string _publicPrefix;
    private readonly string _internalPrefix;
    private readonly string _publicHost;
    private readonly string _publicScheme;

    public InternalAuthorityHandler(Uri publicAuthority, Uri internalAuthority, HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        ArgumentNullException.ThrowIfNull(publicAuthority);
        ArgumentNullException.ThrowIfNull(internalAuthority);
        _publicPrefix = WithTrailingSlash(publicAuthority);
        _internalPrefix = WithTrailingSlash(internalAuthority);
        _publicHost = publicAuthority.Authority;
        _publicScheme = publicAuthority.Scheme;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string? address = request.RequestUri?.AbsoluteUri;
        if (address is not null && address.StartsWith(_publicPrefix, StringComparison.OrdinalIgnoreCase))
        {
            request.RequestUri = new Uri(_internalPrefix + address[_publicPrefix.Length..]);
            request.Headers.Remove("X-Forwarded-Host");
            request.Headers.Remove("X-Forwarded-Proto");
            request.Headers.TryAddWithoutValidation("X-Forwarded-Host", _publicHost);
            request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", _publicScheme);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static string WithTrailingSlash(Uri uri)
    {
        string value = uri.AbsoluteUri;
        return value.EndsWith('/') ? value : value + "/";
    }
}
