// R02: Redis mantiene los contadores distribuidos del rate limiting del Gateway.
// El rechazo se expresa como HTTP 429 con Retry-After para el cliente.

using StackExchange.Redis;

namespace Secunitec.Gateway;

public sealed record RateLimitDecision(
    bool Allowed,
    long RetryAfterSeconds);

public sealed class RedisRateLimiter
{
    private const string IncrementScript = """
local count = redis.call('INCR', KEYS[1])
if count == 1 then
  redis.call('EXPIRE', KEYS[1], ARGV[1])
end
return count
""";

    private readonly IConnectionMultiplexer _redis;

    public RedisRateLimiter(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<RateLimitDecision> CheckAsync(
        string policy,
        string partition,
        int limit,
        TimeSpan window)
    {
        var db = _redis.GetDatabase();
        string key =
            $"secunitec:rate:{policy}:{partition}";

        RedisResult value =
            await db.ScriptEvaluateAsync(
                IncrementScript,
                new[] { (RedisKey)key },
                new[] { (RedisValue)(long)window.TotalSeconds });

        long count = (long)value;

        if (count <= limit)
        {
            return new RateLimitDecision(true, 0);
        }

        TimeSpan? ttl = await db.KeyTimeToLiveAsync(key);

        long retry = ttl is { } duration
            ? Math.Max(1, (long)Math.Ceiling(duration.TotalSeconds))
            : (long)window.TotalSeconds;

        return new RateLimitDecision(false, retry);
    }
}
