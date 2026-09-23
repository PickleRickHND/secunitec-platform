using System.Text.Json;
using Microsoft.Extensions.Logging;
using Secunitec.Billing.Application;
using StackExchange.Redis;

namespace Secunitec.Billing.Infrastructure;

/// <summary>
/// Caché de lecturas en Redis (R08). Cada tenant tiene una versión: invalidar es incrementarla, así las claves
/// viejas dejan de leerse y expiran solas. Si Redis falla se registra un warning y se sigue contra Postgres, en vez
/// de convertir una caída de Redis en un 500 (R18).
/// </summary>
public sealed partial class RedisCache(IConnectionMultiplexer redis, ILogger<RedisCache> logger) : ICache
{
    private static readonly TimeSpan _ttl = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private readonly IDatabase _db = redis.GetDatabase();

    private static string VersionKey(Guid tenantId) => $"billing:{tenantId:N}:version";

    public async Task<T?> Obtener<T>(Guid tenantId, string clave, CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            RedisValue json = await _db.StringGetAsync(await Key(tenantId, clave));
            return json.HasValue ? JsonSerializer.Deserialize<T>(json.ToString(), _json) : null;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or JsonException)
        {
            CacheNoDisponible(logger, nameof(Obtener), ex);
            return null;
        }
    }

    public async Task Guardar<T>(Guid tenantId, string clave, T valor, CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _db.StringSetAsync(await Key(tenantId, clave), JsonSerializer.Serialize(valor, _json), _ttl);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            CacheNoDisponible(logger, nameof(Guardar), ex);
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

    private async Task<RedisKey> Key(Guid tenantId, string clave)
    {
        RedisValue version = await _db.StringGetAsync(VersionKey(tenantId));
        return $"billing:{tenantId:N}:{(version.HasValue ? version.ToString() : "0")}:{clave}";
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Redis no disponible en {Operacion}; se continúa sin caché.")]
    private static partial void CacheNoDisponible(ILogger logger, string operacion, Exception exception);
}
