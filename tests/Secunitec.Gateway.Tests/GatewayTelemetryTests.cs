using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace Secunitec.Gateway.Tests;

// 5.2 (R19): métricas propias del rate limiting y el borde de trazas (TB0).
public sealed class GatewayTelemetryTests : IClassFixture<GatewayFactory>
{
    private const string TraceIdExterno = "0af7651916cd43dd8448eb211c80319c";

    private readonly GatewayFactory _factory;

    public GatewayTelemetryTests(GatewayFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Rechazo429_SeCuentaConSuPoliticaYSuRetryAfter()
    {
        IMeterFactory meters = _factory.Services.GetRequiredService<IMeterFactory>();
        using MetricCollector<long> rechazos = new(meters, GatewayMetrics.MeterName, GatewayMetrics.RateLimitedInstrument);
        using MetricCollector<int> retryAfter = new(meters, GatewayMetrics.MeterName, GatewayMetrics.RetryAfterInstrument);
        using HttpClient client = Client("10.98.0.1");

        for (int i = 0; i < GatewayFactory.TokenEndpointLimit + 2; i++)
        {
            using HttpResponseMessage response = await client.PostAsync("/connect/token", new StringContent(""), Ct);
        }

        Assert.Equal(2, rechazos.GetMeasurementSnapshot().Count(x =>
            Equals(x.Tags[GatewayMetrics.PolicyTag], GatewayRateLimitPolicies.TokenEndpoint) && x.Value == 1));
        Assert.All(retryAfter.GetMeasurementSnapshot(), x => Assert.InRange(x.Value, 1, 60));
        Assert.Equal(2, retryAfter.GetMeasurementSnapshot().Count);
    }

    [Fact]
    public async Task PeticionAceptada_NoSeCuentaComoRechazo()
    {
        IMeterFactory meters = _factory.Services.GetRequiredService<IMeterFactory>();
        using MetricCollector<long> rechazos = new(meters, GatewayMetrics.MeterName, GatewayMetrics.RateLimitedInstrument);
        using HttpClient client = Client("10.98.0.2", GatewayFactory.CreateToken());

        using HttpResponseMessage response = await client.GetAsync("/api/billing/metrica-aceptada", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(rechazos.GetMeasurementSnapshot());
    }

    [Fact]
    public async Task TraceparentDelCliente_NoLlegaAlServicioInterno_TB0()
    {
        using HttpClient client = Client("10.98.0.3", GatewayFactory.CreateToken());
        client.DefaultRequestHeaders.Add("traceparent", $"00-{TraceIdExterno}-b7ad6b7169203331-01");
        client.DefaultRequestHeaders.Add("baggage", "tenant_id=otro");

        using HttpResponseMessage response = await client.GetAsync("/api/billing/traza-externa", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string recibido = _factory.BackendTraceParents["/api/billing/traza-externa"];
        // El servicio interno recibe la traza que abrió el gateway, no la que eligió el cliente.
        Assert.Matches("^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$", recibido);
        Assert.DoesNotContain(TraceIdExterno, recibido, StringComparison.Ordinal);
        Assert.Equal("", _factory.BackendBaggage["/api/billing/traza-externa"]);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Client(string ip, string? token = null)
    {
        HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Ip", ip);
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }
}
