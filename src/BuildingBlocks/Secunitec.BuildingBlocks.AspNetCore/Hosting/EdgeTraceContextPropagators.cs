using System.Diagnostics;
using OpenTelemetry.Context.Propagation;

namespace Secunitec.BuildingBlocks.AspNetCore.Hosting;

/// <summary>
/// TB0: propagador del hosting de ASP.NET Core para el servicio de borde. No lee el contexto de traza ni el baggage
/// del cliente (cada petición de Internet empieza una traza nueva) y sí lo escribe en las llamadas salientes.
/// </summary>
public sealed class EdgeDistributedContextPropagator : DistributedContextPropagator
{
    private readonly DistributedContextPropagator _inner = CreateDefaultPropagator();

    public override IReadOnlyCollection<string> Fields => _inner.Fields;

    public override void Inject(Activity? activity, object? carrier, PropagatorSetterCallback? setter) =>
        _inner.Inject(activity, carrier, setter);

    public override void ExtractTraceIdAndState(
        object? carrier, PropagatorGetterCallback? getter, out string? traceId, out string? traceState)
    {
        traceId = null;
        traceState = null;
    }

    public override IEnumerable<KeyValuePair<string, string?>>? ExtractBaggage(object? carrier, PropagatorGetterCallback? getter) => null;
}

/// <summary>
/// TB0: la misma regla para la instrumentación de OpenTelemetry, que extrae por su cuenta con el propagador global.
/// </summary>
public sealed class EdgeTextMapPropagator : TextMapPropagator
{
    private readonly TextMapPropagator _inner;

    public EdgeTextMapPropagator(TextMapPropagator inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public override ISet<string>? Fields => _inner.Fields;

    public override PropagationContext Extract<T>(PropagationContext context, T carrier, Func<T, string, IEnumerable<string>?> getter) =>
        context;

    public override void Inject<T>(PropagationContext context, T carrier, Action<T, string, string> setter) =>
        _inner.Inject(context, carrier, setter);
}
