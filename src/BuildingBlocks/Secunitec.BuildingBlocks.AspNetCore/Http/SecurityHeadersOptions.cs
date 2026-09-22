namespace Secunitec.BuildingBlocks.AspNetCore.Http;

/// <summary>Valores de las cabeceras de seguridad. Los defaults son seguros para APIs JSON.</summary>
public sealed class SecurityHeadersOptions
{
    /// <summary>
    /// <c>Content-Security-Policy</c>. Para una API sin HTML basta con bloquear todo; Identity (Razor) y el
    /// Front-End (nginx) definen la suya. <c>null</c> o vacío omite la cabecera.
    /// </summary>
    public string? ContentSecurityPolicy { get; set; } = "default-src 'none'; frame-ancestors 'none'";

    /// <summary><c>Referrer-Policy</c>.</summary>
    public string ReferrerPolicy { get; set; } = "no-referrer";

    /// <summary><c>Permissions-Policy</c>: se deshabilitan las APIs del navegador que ningún servicio usa.</summary>
    public string PermissionsPolicy { get; set; } = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

    /// <summary><c>X-Frame-Options</c>. Redundante con <c>frame-ancestors</c> pero lo entienden navegadores viejos.</summary>
    public string FrameOptions { get; set; } = "DENY";

    /// <summary>Agrega <c>Cache-Control: no-store</c> para que respuestas con datos no queden en cachés intermedias.</summary>
    public bool NoStore { get; set; } = true;
}
