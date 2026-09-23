using Secunitec.Billing.Application;
using Secunitec.BuildingBlocks.AspNetCore.Http;

namespace Secunitec.Billing.Api;

/// <summary>IP del cliente (ya reemplazada por UseForwardedHeaders) y correlation id de la petición, para la auditoría.</summary>
public sealed class HttpRequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    public string? Ip => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? CorrelationId => accessor.HttpContext?.GetCorrelationId();
}
