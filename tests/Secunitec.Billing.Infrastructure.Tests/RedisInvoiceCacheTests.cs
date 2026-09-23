using Microsoft.Extensions.Logging.Abstractions;
using Secunitec.Billing.Application;
using StackExchange.Redis;

namespace Secunitec.Billing.Infrastructure.Tests;

// Regresión: con Redis caído, listar, crear, emitir y anular respondían 500 aunque la caché es opcional.
public sealed class RedisInvoiceCacheTests
{
    // Puerto 1 en loopback: conexión rechazada al instante, sin depender de Docker.
    private const string RedisInalcanzable = "127.0.0.1:1,abortConnect=false,connectTimeout=200,asyncTimeout=200,syncTimeout=200";

    [Fact]
    public async Task RedisCaido_TodasLasOperaciones_NoLanzan()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(RedisInalcanzable);
        RedisInvoiceCache cache = new(redis, NullLogger<RedisInvoiceCache>.Instance);
        Guid tenant = Guid.NewGuid();

        Assert.Null(await cache.ObtenerListado(tenant, null, ct));
        await cache.GuardarListado(tenant, null, Array.Empty<FacturaResumen>(), ct);
        await cache.Invalidar(tenant, ct);
    }
}
