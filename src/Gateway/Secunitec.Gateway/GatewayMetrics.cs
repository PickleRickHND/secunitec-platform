// R02 + R19 (5.2): métricas propias del rate limiting para el dashboard "Resiliencia del gateway".
// En Prometheus: secunitec_gateway_rate_limited_total y secunitec_gateway_retry_after_seconds_*.
// Solo la política como etiqueta: ni IP ni sub (datos personales y cardinalidad sin límite; TB3).

using System.Diagnostics.Metrics;

namespace Secunitec.Gateway;

public sealed class GatewayMetrics
{
    public const string MeterName = "Secunitec.Gateway";
    public const string RateLimitedInstrument = "secunitec.gateway.rate_limited";
    public const string RetryAfterInstrument = "secunitec.gateway.retry_after";
    public const string PolicyTag = "policy";

    private readonly Counter<long> _rateLimited;
    private readonly Histogram<int> _retryAfter;

    public GatewayMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        Meter meter = meterFactory.Create(MeterName);
        _rateLimited = meter.CreateCounter<long>(
            RateLimitedInstrument, unit: "{request}", description: "Peticiones rechazadas con 429 por el rate limiting");
        _retryAfter = meter.CreateHistogram<int>(
            RetryAfterInstrument,
            unit: "s",
            description: "Retry-After enviado en cada 429",
            advice: new InstrumentAdvice<int> { HistogramBucketBoundaries = [1, 2, 5, 10, 15, 30, 45, 60] });
    }

    /// <summary>Un 429: cuenta el rechazo y registra el <c>Retry-After</c> que se le envió al cliente.</summary>
    public void RateLimited(string policy, int retryAfterSeconds)
    {
        KeyValuePair<string, object?> tag = new(PolicyTag, policy);
        _rateLimited.Add(1, tag);
        _retryAfter.Record(retryAfterSeconds, tag);
    }
}
