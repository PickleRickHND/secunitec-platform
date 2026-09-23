using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Secunitec.Billing.Application;
using StackExchange.Redis;

namespace Secunitec.Billing.Infrastructure.Tests;

public sealed class RedisCacheTests
{
    // Puerto 1 en loopback: conexión rechazada al instante, sin depender de Docker.
    private const string RedisInalcanzable = "127.0.0.1:1,abortConnect=false,connectTimeout=200,asyncTimeout=200,syncTimeout=200";
    private const string Password = "clave-de-prueba-de-redis";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Regresión: con Redis caído, listar, crear, emitir y anular respondían 500 aunque la caché es opcional.
    [Fact]
    public async Task RedisCaido_TodasLasOperaciones_NoLanzan()
    {
        await using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(RedisInalcanzable);
        RedisCache cache = new(redis, NullLogger<RedisCache>.Instance);
        Guid tenant = Guid.NewGuid();

        Assert.Null(await cache.Obtener<Pagina<ClienteDto>>(tenant, "clave", Ct));
        await cache.Guardar(tenant, "clave", new Pagina<ClienteDto>([], 0, 1, 20), Ct);
        await cache.Invalidar(tenant, Ct);
    }

    // 2.1: con el redis.conf real (CONFIG, FLUSHALL, FLUSHDB y DEBUG deshabilitados), StackExchange.Redis conecta igual
    // (tolera que CONFIG falle en el handshake), la caché funciona y los comandos peligrosos no existen en el servidor.
    [Fact]
    public async Task RedisConfReal_ConectaYBloqueaComandosPeligrosos()
    {
        await using IContainer container = await StartRedisAsync();

        string endpoint = $"{container.Hostname}:{container.GetMappedPublicPort(6379)}";
        await using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(
            $"{endpoint},password={Password},abortConnect=false");
        RedisCache cache = new(redis, NullLogger<RedisCache>.Instance);
        Guid tenant = Guid.NewGuid();
        Pagina<ClienteDto> pagina = new([new ClienteDto(Guid.NewGuid(), "Cliente", null, null)], 1, 1, 20);

        await cache.Guardar(tenant, "clientes", pagina, Ct);
        Pagina<ClienteDto>? leida = await cache.Obtener<Pagina<ClienteDto>>(tenant, "clientes", Ct);
        await cache.Invalidar(tenant, Ct);

        Assert.True(redis.IsConnected);
        Assert.Equal("Cliente", Assert.Single(leida!.Items).Nombre);
        Assert.Null(await cache.Obtener<Pagina<ClienteDto>>(tenant, "clientes", Ct));
        IDatabase db = redis.GetDatabase();
        // Se comprueba en el servidor con redis-cli: StackExchange.Redis ya bloquea CONFIG en el cliente sin allowAdmin.
        foreach (string comando in new[] { "CONFIG GET maxmemory", "FLUSHALL", "FLUSHDB", "DEBUG SLEEP 0" })
        {
            ExecResult result = await container.ExecAsync(
                ["redis-cli", "--no-auth-warning", "-a", Password, .. comando.Split(' ')], Ct);
            Assert.Contains("unknown command", result.Stdout + result.Stderr, StringComparison.OrdinalIgnoreCase);
        }

        // Los scripts Lua (los usa RedisRateLimiting en el gateway) siguen disponibles.
        Assert.Equal(2, (int)await db.ScriptEvaluateAsync("return redis.call('INCRBY', KEYS[1], 2)", ["secunitec:test:lua"]));
    }

    private static async Task<IContainer> StartRedisAsync()
    {
        IContainer container = new ContainerBuilder("redis:7-alpine")
            .WithResourceMapping(new FileInfo(Path.Combine(RepoRoot(), "infra", "redis", "redis.conf")), "/usr/local/etc/redis/")
            .WithCommand("redis-server", "/usr/local/etc/redis/redis.conf", "--requirepass", Password)
            .WithPortBinding(6379, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Ready to accept connections"))
            .Build();
        try
        {
            await container.StartAsync(Ct);
        }
        catch (Exception ex) when (!string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            await container.DisposeAsync();
            Assert.Skip("Docker no está disponible para Testcontainers: " + ex.Message);
        }

        return container;
    }

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Secunitec.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }
}
