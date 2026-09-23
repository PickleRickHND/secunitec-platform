using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Secunitec.Gateway.Tests;

// Contrato del gateway (docs/PLAN.md §8 y criterio de done de 3.2): 401, 403, 429 con Retry-After y cabeceras.
public sealed class GatewayContractTests : IClassFixture<GatewayFactory>
{
    private readonly GatewayFactory _factory;

    public GatewayContractTests(GatewayFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Billing_SinToken_Devuelve401SinLlegarAlServicio()
    {
        using HttpClient client = Client(NewIp());

        using HttpResponseMessage response = await client.GetAsync("/api/billing/sin-token", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(_factory.BackendHits.ContainsKey("/api/billing/sin-token"));
    }

    [Fact]
    public async Task Billing_TokenSinTenant_Devuelve403()
    {
        using HttpClient client = Client(NewIp(), GatewayFactory.CreateToken(withTenant: false));

        using HttpResponseMessage response = await client.GetAsync("/api/billing/sin-tenant", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(_factory.BackendHits.ContainsKey("/api/billing/sin-tenant"));
    }

    [Fact]
    public async Task Billing_TokenValido_LlegaAlServicioSinCabecerasDeTecnologia()
    {
        using HttpClient client = Client(NewIp(), GatewayFactory.CreateToken());

        using HttpResponseMessage response = await client.GetAsync("/api/billing/facturas", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // R06: el backend manda Server y X-Powered-By; el gateway no los deja pasar.
        Assert.False(response.Headers.Contains("Server"));
        Assert.False(response.Headers.Contains("X-Powered-By"));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task Billing_CorrelationId_SePropagaAlServicio()
    {
        using HttpClient client = Client(NewIp(), GatewayFactory.CreateToken());
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "prueba-correlacion-1");

        using HttpResponseMessage response = await client.GetAsync("/api/billing/correlacion", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("prueba-correlacion-1", response.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal("prueba-correlacion-1", _factory.BackendCorrelationIds["/api/billing/correlacion"]);
    }

    [Theory]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("/connect/authorize")]
    [InlineData("/account/login")]
    public async Task RutasOidc_SinToken_SonAnonimas(string path)
    {
        using HttpClient client = Client(NewIp());

        using HttpResponseMessage response = await client.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _factory.BackendHits[path]);
    }

    [Fact]
    public async Task TokenEndpoint_SextaPeticion_Devuelve429ConRetryAfterYJson()
    {
        using HttpClient client = Client(NewIp());
        int before = _factory.BackendHits.GetValueOrDefault("/connect/token");

        for (int i = 0; i < GatewayFactory.TokenEndpointLimit; i++)
        {
            using HttpResponseMessage allowed = await client.PostAsync("/connect/token", new StringContent(""), Ct);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using HttpResponseMessage rejected = await client.PostAsync("/connect/token", new StringContent(""), Ct);

        await AssertRateLimited(rejected);
        // R02: la petición rechazada no llega al servicio interno.
        Assert.Equal(before + GatewayFactory.TokenEndpointLimit, _factory.BackendHits["/connect/token"]);
    }

    [Fact]
    public async Task Billing_RafagaDelMismoSub_Devuelve429()
    {
        string token = GatewayFactory.CreateToken(Guid.NewGuid());

        for (int i = 0; i < GatewayFactory.UserLimit; i++)
        {
            // Cada petición sale de una IP distinta: el límite es por sub, no por IP.
            using HttpClient allowedClient = Client(NewIp(), token);
            using HttpResponseMessage allowed = await allowedClient.GetAsync("/api/billing/rafaga", Ct);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using HttpClient client = Client(NewIp(), token);
        using HttpResponseMessage rejected = await client.GetAsync("/api/billing/rafaga", Ct);

        await AssertRateLimited(rejected);
    }

    [Fact]
    public async Task Billing_RafagaConTokensInvalidos_SeLimitaPorIp()
    {
        using HttpClient client = Client(NewIp(), "token.invalido.firma");
        List<HttpStatusCode> statuses = [];

        for (int i = 0; i <= GatewayFactory.UserLimit; i++)
        {
            using HttpResponseMessage response = await client.GetAsync("/api/billing/invalido", Ct);
            statuses.Add(response.StatusCode);
        }

        Assert.All(statuses.Take(GatewayFactory.UserLimit), status => Assert.Equal(HttpStatusCode.Unauthorized, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task Health_ConRedisCaido_Devuelve200()
    {
        // La fábrica apunta Redis a un puerto cerrado: el rate limiting usa el respaldo en memoria, sin 500.
        using HttpClient client = Client(NewIp());

        using HttpResponseMessage response = await client.GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static int _nextIp;

    // Una IP distinta por cliente: las particiones del rate limiting no se comparten entre tests.
    private static string NewIp()
    {
        int n = Interlocked.Increment(ref _nextIp);
        return $"10.99.{n / 250}.{(n % 250) + 1}";
    }

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

    private static async Task AssertRateLimited(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(int.Parse(response.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture) > 0);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("rate_limited", body.RootElement.GetProperty("error").GetString());
    }
}
