using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OpenTelemetry.Context.Propagation;
using Secunitec.BuildingBlocks.AspNetCore.Hosting;
using Secunitec.BuildingBlocks.AspNetCore.Http;
using Secunitec.BuildingBlocks.Http;
using Secunitec.BuildingBlocks.Tests.Support;

namespace Secunitec.BuildingBlocks.Tests.Hosting;

// 5.2 (R19, TB0): correlation id en la traza y el borde que no acepta el contexto de traza del cliente.
public sealed class TelemetryTests
{
    private const string TraceParentExterno = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    [Fact]
    public async Task CorrelationId_QuedaComoAtributoDeLaTrazaDeLaPeticion()
    {
        ConcurrentBag<Activity> detenidas = [];
        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = detenidas.Add,
        };
        ActivitySource.AddActivityListener(listener);
        await using TestApp app = await TestApp.StartAsync(routes => routes.MapGet("/traza", () => Results.Ok()).AllowAnonymous());
        using HttpRequestMessage request = new(HttpMethod.Get, "/traza");
        request.Headers.Add(CorrelationId.HeaderName, "traza-correlacion-1");

        using HttpResponseMessage response = await app.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        Assert.Contains(detenidas, activity =>
            (activity.GetTagItem(CorrelationIdMiddleware.ActivityTag) as string) == "traza-correlacion-1");
    }

    [Fact]
    public void PropagadorDelHosting_NoLeeElTraceparentDelCliente_TB0()
    {
        EdgeDistributedContextPropagator propagator = new();
        Dictionary<string, string> entrantes = new()
        {
            ["traceparent"] = TraceParentExterno,
            ["baggage"] = "tenant_id=otro",
        };

        propagator.ExtractTraceIdAndState(entrantes, Leer, out string? traceId, out string? traceState);

        Assert.Null(traceId);
        Assert.Null(traceState);
        Assert.Null(propagator.ExtractBaggage(entrantes, Leer));
    }

    [Fact]
    public void PropagadorDelHosting_SiEscribeElContextoHaciaAdentro()
    {
        EdgeDistributedContextPropagator propagator = new();
        using Activity activity = new Activity("gateway").SetIdFormat(ActivityIdFormat.W3C).Start();
        Dictionary<string, string> salientes = [];

        propagator.Inject(activity, salientes, (carrier, name, value) => ((Dictionary<string, string>)carrier!)[name] = value);

        Assert.Equal(activity.Id, salientes["traceparent"]);
    }

    [Fact]
    public void PropagadorDeOpenTelemetry_DevuelveElContextoSinExtraer_TB0()
    {
        EdgeTextMapPropagator propagator = new(new CompositeTextMapPropagator([new TraceContextPropagator(), new BaggagePropagator()]));
        Dictionary<string, string> entrantes = new() { ["traceparent"] = TraceParentExterno, ["baggage"] = "tenant_id=otro" };

        PropagationContext contexto = propagator.Extract(default, entrantes, (carrier, name) =>
            carrier.TryGetValue(name, out string? value) ? [value] : []);

        Assert.Equal(default, contexto.ActivityContext);
        Assert.Equal(0, contexto.Baggage.Count);
    }

    private static void Leer(object? carrier, string name, out string? value, out IEnumerable<string>? values)
    {
        values = null;
        value = ((Dictionary<string, string>)carrier!).GetValueOrDefault(name);
    }
}
