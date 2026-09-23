// R02: rate limiting L7 con Microsoft.AspNetCore.RateLimiting y contadores en Redis (docs/PLAN.md §0 y §6).
// Si Redis no responde se usa un limitador en memoria (§10): el gateway sigue respondiendo 429 controlados
// en vez de 500, aunque cada instancia cuente por separado mientras dure la caída.

using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using RedisRateLimiting;
using Secunitec.BuildingBlocks.Security;
using StackExchange.Redis;

namespace Secunitec.Gateway;

/// <summary>Nombres de las políticas que las rutas de YARP asignan con <c>RateLimiterPolicy</c>.</summary>
public static class GatewayRateLimitPolicies
{
    /// <summary>Estricta para <c>/connect/token</c>, por IP.</summary>
    public const string TokenEndpoint = "token-endpoint";

    /// <summary>Rutas anónimas del protocolo OIDC, por IP.</summary>
    public const string AnonymousByIp = "anon-by-ip";

    /// <summary>Rutas autenticadas, por <c>sub</c> (o <c>client_id</c>); sin token válido cae a la IP.</summary>
    public const string UserBySub = "user-by-sub";
}

/// <summary>Límites configurables en la sección <c>RateLimiting</c>.</summary>
public sealed class GatewayRateLimitOptions
{
    public int TokenEndpointPermitLimit { get; set; } = 5;
    public int AnonymousPermitLimit { get; set; } = 60;
    public int UserPermitLimit { get; set; } = 60;
    public int WindowSeconds { get; set; } = 60;
}

public static class GatewayRateLimitingExtensions
{
    public static IServiceCollection AddSecunitecRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<GatewayRateLimitOptions>(configuration.GetSection("RateLimiting"));
        services.AddSingleton<GatewayMetrics>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(GatewayRateLimitPolicies.TokenEndpoint, http =>
                Partition(http, GatewayRateLimitPolicies.TokenEndpoint, ByIp(http), o => o.TokenEndpointPermitLimit));
            options.AddPolicy(GatewayRateLimitPolicies.AnonymousByIp, http =>
                Partition(http, GatewayRateLimitPolicies.AnonymousByIp, ByIp(http), o => o.AnonymousPermitLimit));
            options.AddPolicy(GatewayRateLimitPolicies.UserBySub, http =>
                Partition(http, GatewayRateLimitPolicies.UserBySub, BySubject(http), o => o.UserPermitLimit));

            // R02: 429 + Retry-After + JSON; la petición nunca llega al servicio interno.
            options.OnRejected = async (context, cancellationToken) =>
            {
                IServiceProvider requestServices = context.HttpContext.RequestServices;
                int window = requestServices
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<GatewayRateLimitOptions>>().Value.WindowSeconds;
                int retryAfter = RetryAfterSeconds(context.Lease, window);
                requestServices.GetRequiredService<GatewayMetrics>().RateLimited(PolicyName(context.HttpContext), retryAfter);
                context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new { error = "rate_limited", retry_after = retryAfter }, cancellationToken);
            };
        });

        return services;
    }

    /// <summary>Política del endpoint (la ruta de YARP o <c>/health</c> la declaran con <c>EnableRateLimiting</c>).</summary>
    private static string PolicyName(HttpContext http) =>
        http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ?? "desconocida";

    private static string ByIp(HttpContext http) =>
        "ip:" + (http.Connection.RemoteIpAddress?.ToString() ?? "desconocida");

    private static string BySubject(HttpContext http)
    {
        // Un token inválido deja al usuario como anónimo y cae a la partición por IP: las ráfagas con tokens
        // basura también reciben 429 antes que el 401.
        if (http.User.Identity?.IsAuthenticated != true)
        {
            return ByIp(http);
        }

        string? subject = http.User.FindFirstValue(SecunitecClaims.Subject);
        return subject is not null ? "sub:" + subject : "client:" + http.User.FindFirstValue(SecunitecClaims.ClientId);
    }

    private static RateLimitPartition<string> Partition(
        HttpContext http, string policy, string subject, Func<GatewayRateLimitOptions, int> permitLimit)
    {
        GatewayRateLimitOptions limits = http.RequestServices
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<GatewayRateLimitOptions>>().Value;
        IConnectionMultiplexer redis = http.RequestServices.GetRequiredService<IConnectionMultiplexer>();
        ILogger logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Secunitec.Gateway.RateLimiting");
        int limit = permitLimit(limits);
        TimeSpan window = TimeSpan.FromSeconds(limits.WindowSeconds);

        return RateLimitPartition.Get($"secunitec:rl:{policy}:{subject}", key => new ResilientRateLimiter(
            new RedisFixedWindowRateLimiter<string>(key, new RedisFixedWindowRateLimiterOptions
            {
                ConnectionMultiplexerFactory = () => redis,
                PermitLimit = limit,
                Window = window,
            }),
            new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true,
            }),
            redis,
            logger));
    }

    private static int RetryAfterSeconds(RateLimitLease lease, int windowSeconds)
    {
        // El limitador de Redis y el de memoria exponen Retry-After con tipos distintos (segundos o TimeSpan).
        foreach (string name in lease.MetadataNames)
        {
            if (string.Equals(name, MetadataName.RetryAfter.Name, StringComparison.OrdinalIgnoreCase) &&
                lease.TryGetMetadata(name, out object? value))
            {
                double seconds = value switch
                {
                    TimeSpan span => span.TotalSeconds,
                    int integer => integer,
                    long integer => integer,
                    _ => windowSeconds,
                };
                return Math.Max(1, (int)Math.Ceiling(seconds));
            }
        }

        return windowSeconds;
    }
}

/// <summary>
/// Usa el limitador de Redis mientras haya conexión y el de memoria si Redis está caído o falla la operación.
/// </summary>
internal sealed partial class ResilientRateLimiter : RateLimiter
{
    private static long _lastWarningTicks;

    private readonly RateLimiter _redis;
    private readonly RateLimiter _memory;
    private readonly IConnectionMultiplexer _connection;
    private readonly ILogger _logger;

    public ResilientRateLimiter(RateLimiter redis, RateLimiter memory, IConnectionMultiplexer connection, ILogger logger)
    {
        _redis = redis;
        _memory = memory;
        _connection = connection;
        _logger = logger;
    }

    // El estado real vive en Redis: la partición puede descartarse cuando el limitador en memoria está ocioso.
    public override TimeSpan? IdleDuration => _memory.IdleDuration;

    public override RateLimiterStatistics? GetStatistics() =>
        _connection.IsConnected ? _redis.GetStatistics() : _memory.GetStatistics();

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        if (_connection.IsConnected)
        {
            try
            {
                return _redis.AttemptAcquire(permitCount);
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                WarnFallback(ex);
                return _memory.AttemptAcquire(permitCount);
            }
        }

        WarnFallback(null);
        return _memory.AttemptAcquire(permitCount);
    }

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        if (_connection.IsConnected)
        {
            try
            {
                return await _redis.AcquireAsync(permitCount, cancellationToken);
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                WarnFallback(ex);
                return await _memory.AcquireAsync(permitCount, cancellationToken);
            }
        }

        WarnFallback(null);
        return await _memory.AcquireAsync(permitCount, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _redis.Dispose();
            _memory.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _redis.DisposeAsync();
        await _memory.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    private void WarnFallback(Exception? exception)
    {
        // Un warning cada 30 s como máximo: durante una caída cada petición pasaría por aquí.
        long now = Environment.TickCount64;
        long last = Interlocked.Read(ref _lastWarningTicks);
        if (now - last >= 30_000 && Interlocked.CompareExchange(ref _lastWarningTicks, now, last) == last)
        {
            RedisUnavailable(_logger, exception);
        }
    }

    [LoggerMessage(EventId = 3201, Level = LogLevel.Warning,
        Message = "Redis no disponible para el rate limiting; se usa el limitador en memoria.")]
    private static partial void RedisUnavailable(ILogger logger, Exception? exception);
}
