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
    public async Task Cors_PreflightDelSpa_SeRespondeSinLlegarAlServicioNiGastarCupo()
    {
        string ip = NewIp();
        using HttpClient client = Client(ip);

        // Más preflights que el cupo de la partición: ninguno cuenta ni llega al servicio.
        for (int i = 0; i <= GatewayFactory.UserLimit; i++)
        {
            using HttpResponseMessage preflight = await client.SendAsync(Preflight("/api/billing/preflight", GatewayFactory.SpaOrigin), Ct);
            Assert.Equal(HttpStatusCode.NoContent, preflight.StatusCode);
            Assert.Equal(GatewayFactory.SpaOrigin, preflight.Headers.GetValues("Access-Control-Allow-Origin").Single());
        }

        using HttpClient withToken = Client(ip, GatewayFactory.CreateToken());
        using HttpResponseMessage real = await withToken.GetAsync("/api/billing/preflight", Ct);

        Assert.Equal(HttpStatusCode.OK, real.StatusCode);
        Assert.Equal(1, _factory.BackendHits["/api/billing/preflight"]);
    }

    [Fact]
    public async Task Cors_OrigenNoPermitido_NoRecibeAllowOrigin()
    {
        using HttpClient client = Client(NewIp());

        using HttpResponseMessage preflight = await client.SendAsync(Preflight("/api/billing/facturas", "https://atacante.test"), Ct);

        Assert.False(preflight.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Cors_401Y429_LlevanAllowOriginYExponenRetryAfter()
    {
        using HttpClient client = Client(NewIp());
        client.DefaultRequestHeaders.Add("Origin", GatewayFactory.SpaOrigin);
        List<HttpResponseMessage> responses = [];

        try
        {
            for (int i = 0; i <= GatewayFactory.UserLimit; i++)
            {
                responses.Add(await client.GetAsync("/api/billing/cors", Ct));
            }

            HttpResponseMessage unauthorized = responses[0];
            HttpResponseMessage limited = responses[^1];
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
            Assert.Equal(GatewayFactory.SpaOrigin, unauthorized.Headers.GetValues("Access-Control-Allow-Origin").Single());
            await AssertRateLimited(limited);
            Assert.Equal(GatewayFactory.SpaOrigin, limited.Headers.GetValues("Access-Control-Allow-Origin").Single());
            Assert.Contains("Retry-After", limited.Headers.GetValues("Access-Control-Expose-Headers").Single(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            responses.ForEach(response => response.Dispose());
        }
    }

    [Fact]
    public async Task Cors_PaginasDeCuenta_NoAdmitenOtroOrigen()
    {
        // /account es navegación de página completa: no necesita CORS y no lo declara. Ruta propia: sin CorsPolicy el
        // preflight se reenvía al backend y contaría como visita en las rutas que miden otros tests.
        using HttpClient client = Client(NewIp());

        using HttpResponseMessage preflight = await client.SendAsync(Preflight("/account/preflight-sin-cors", GatewayFactory.SpaOrigin), Ct);

        Assert.False(preflight.Headers.Contains("Access-Control-Allow-Origin"));
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

    private static HttpRequestMessage Preflight(string path, string origin)
    {
        HttpRequestMessage request = new(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,x-correlation-id");
        return request;
    }

    private static async Task AssertRateLimited(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(int.Parse(response.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture) > 0);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("rate_limited", body.RootElement.GetProperty("error").GetString());
    }
}
