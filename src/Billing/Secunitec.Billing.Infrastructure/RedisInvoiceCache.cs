using System.Text.Json;
using Microsoft.Extensions.Logging;
using Secunitec.Billing.Application;
using StackExchange.Redis;

namespace Secunitec.Billing.Infrastructure;

/// <summary>
/// Caché del listado de facturas. Es una optimización: si Redis falla se registra un warning y se sigue contra
/// Postgres, en vez de convertir una caída de Redis en un 500 (R18).
/// </summary>
public sealed partial class RedisInvoiceCache(IConnectionMultiplexer redis, ILogger<RedisInvoiceCache> logger) : IInvoiceCache
{
    private readonly IDatabase _db = redis.GetDatabase();

    private static string VersionKey(Guid tenantId) => $"billing:{tenantId:N}:version";

    private async Task<RedisKey> ListKey(Guid tenantId, Guid? clienteId)
    {
        RedisValue version = await _db.StringGetAsync(VersionKey(tenantId));
        return $"billing:{tenantId:N}:{(clienteId.HasValue ? clienteId.Value.ToString("N") : "all")}:{(version.HasValue ? version.ToString() : "0")}";
    }

    public async Task<IReadOnlyList<FacturaResumen>?> ObtenerListado(Guid tenantId, Guid? clienteId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            RedisValue json = await _db.StringGetAsync(await ListKey(tenantId, clienteId));
            return json.HasValue ? JsonSerializer.Deserialize<FacturaResumen[]>(json.ToString()) : null;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            CacheNoDisponible(logger, nameof(ObtenerListado), ex);
            return null;
        }
    }

    public async Task GuardarListado(Guid tenantId, Guid? clienteId, IReadOnlyList<FacturaResumen> facturas, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _db.StringSetAsync(await ListKey(tenantId, clienteId), JsonSerializer.Serialize(facturas), TimeSpan.FromSeconds(30));
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            CacheNoDisponible(logger, nameof(GuardarListado), ex);
        }
    }

    public async Task Invalidar(Guid tenantId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _db.StringIncrementAsync(VersionKey(tenantId));
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            // La escritura en Postgres ya se confirmó; en el peor caso el listado viejo vive hasta su TTL de 30 s.
            CacheNoDisponible(logger, nameof(Invalidar), ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Redis no disponible en {Operacion}; se continúa sin caché.")]
    private static partial void CacheNoDisponible(ILogger logger, string operacion, Exception exception);
}
