using System.Text.Json;
using Secunitec.Billing.Application;
using StackExchange.Redis;

namespace Secunitec.Billing.Infrastructure;

public sealed class RedisInvoiceCache(IConnectionMultiplexer redis) : IInvoiceCache
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
        RedisValue json = await _db.StringGetAsync(await ListKey(tenantId, clienteId));
        return json.HasValue ? JsonSerializer.Deserialize<FacturaResumen[]>(json.ToString()) : null;
    }

    public async Task GuardarListado(Guid tenantId, Guid? clienteId, IReadOnlyList<FacturaResumen> facturas, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _db.StringSetAsync(await ListKey(tenantId, clienteId), JsonSerializer.Serialize(facturas), TimeSpan.FromSeconds(30));
    }

    public async Task Invalidar(Guid tenantId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _db.StringIncrementAsync(VersionKey(tenantId));
    }
}
