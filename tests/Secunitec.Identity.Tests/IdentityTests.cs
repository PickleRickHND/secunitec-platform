using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using MongoDB.Bson;
using MongoDB.Driver;
using Secunitec.BuildingBlocks.Security;

namespace Secunitec.Identity.Tests;

// Criterio de done de 3.1 (docs/PLAN.md §7 H2 y README): discovery, client credentials con los claims del contrato,
// login fallido auditado y lockout; más las regresiones de la revisión del PR #3.
public sealed partial class IdentityTests : IClassFixture<IdentityFactory>
{
    private const string UserPassword = "Usuario!Prueba123";

    private readonly IdentityFactory _factory;

    public IdentityTests(IdentityFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Discovery_Anonimo_Devuelve200ConEndpointsPublicos()
    {
        SkipWithoutDocker();
        using HttpClient client = _factory.CreateHttpsClient();

        using HttpResponseMessage response = await client.GetAsync("/.well-known/openid-configuration", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument discovery = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(IdentityFactory.Issuer, discovery.RootElement.GetProperty("issuer").GetString());
        Assert.StartsWith(IdentityFactory.Issuer, discovery.RootElement.GetProperty("authorization_endpoint").GetString(), StringComparison.Ordinal);
        Assert.StartsWith(IdentityFactory.Issuer, discovery.RootElement.GetProperty("jwks_uri").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClientCredentials_Jmeter_EmiteTokenConClaimsDelContrato()
    {
        SkipWithoutDocker();
        using HttpClient client = _factory.CreateHttpsClient();

        using HttpResponseMessage response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = ConnectEndpoints.JmeterClientId,
            ["client_secret"] = IdentityFactory.JmeterSecret,
            ["scope"] = SecunitecAudiences.Billing,
        }), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        JsonWebToken token = new(body.RootElement.GetProperty("access_token").GetString());
        Assert.Equal("RS256", token.Alg);
        Assert.Equal(IdentityFactory.Issuer, token.Issuer);
        Assert.Contains(SecunitecAudiences.Billing, token.Audiences);
        // Contrato del token (PLAN §7.1): sub GUID, también en tokens de aplicación.
        Assert.True(Guid.TryParse(token.Subject, out _));
        Assert.Equal(SecunitecRoles.Facturador, token.GetClaim(SecunitecClaims.Role).Value);
        Assert.Equal(IdentityFactory.TenantId.ToString(), token.GetClaim(SecunitecClaims.TenantId).Value);
        Assert.Equal(ConnectEndpoints.JmeterClientId, token.GetClaim(SecunitecClaims.ClientId).Value);
    }

    [Fact]
    public async Task Login_ContrasenaIncorrecta_QuedaAuditadoConIpYCorrelationId()
    {
        SkipWithoutDocker();
        string email = await CreateUserAsync();
        using HttpClient client = _factory.CreateHttpsClient();
        string correlationId = "login-fallido-" + Guid.NewGuid().ToString("N")[..12];

        using HttpResponseMessage response = await PostLoginAsync(client, email, "Incorrecta!123", "/",
            headers: new() { ["X-Forwarded-For"] = "203.0.113.9", ["X-Correlation-Id"] = correlationId });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        BsonDocument audit = await _factory.AuditEvents
            .Find(Builders<BsonDocument>.Filter.Eq("correlationId", correlationId)).SingleAsync(Ct);
        Assert.Equal("login", audit["action"].AsString);
        Assert.False(audit["success"].AsBoolean);
        // La IP es la del cliente (X-Forwarded-For del gateway), no la del gateway.
        Assert.Equal("203.0.113.9", audit["ip"].AsString);
    }

    [Fact]
    public async Task Login_SextoIntentoTrasCincoFallidos_Devuelve423YQuedaAuditado()
    {
        SkipWithoutDocker();
        string email = await CreateUserAsync();
        using HttpClient client = _factory.CreateHttpsClient();
        List<HttpStatusCode> statuses = [];

        for (int attempt = 1; attempt <= 5; attempt++)
        {
            using HttpResponseMessage failed = await PostLoginAsync(client, email, "Incorrecta!123", "/");
            statuses.Add(failed.StatusCode);
        }

        // Sexto intento, ahora con la contraseña correcta: la cuenta ya está bloqueada.
        using HttpResponseMessage sixth = await PostLoginAsync(client, email, UserPassword, "/");

        Assert.All(statuses.Take(4), status => Assert.Equal(HttpStatusCode.Unauthorized, status));
        Assert.Equal((HttpStatusCode)StatusCodes423, statuses[4]);
        Assert.Equal((HttpStatusCode)StatusCodes423, sixth.StatusCode);
        long lockedEvents = await _factory.AuditEvents.CountDocumentsAsync(
            Builders<BsonDocument>.Filter.Eq("action", "login_locked_out"), cancellationToken: Ct);
        Assert.True(lockedEvents >= 2);
    }

    [Fact]
    public async Task Login_Exitoso_EmiteCookieHostSecure()
    {
        SkipWithoutDocker();
        string email = await CreateUserAsync();
        using HttpClient client = _factory.CreateHttpsClient();

        using HttpResponseMessage response = await PostLoginAsync(client, email, UserPassword, "/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        string cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-Secunitec.Identity=", StringComparison.Ordinal));
        // Sin Secure, los navegadores descartan cualquier cookie __Host-.
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/\\evil.example", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("/connect/authorize?client_id=spa-secunitec", "/connect/authorize?client_id=spa-secunitec")]
    public async Task Login_Exitoso_SoloRedirigeAUrlsLocales(string returnUrl, string expectedLocation)
    {
        SkipWithoutDocker();
        string email = await CreateUserAsync();
        using HttpClient client = _factory.CreateHttpsClient();

        using HttpResponseMessage response = await PostLoginAsync(client, email, UserPassword, returnUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expectedLocation, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Login_SinTokenAntiforgery_Devuelve400()
    {
        SkipWithoutDocker();
        using HttpClient client = _factory.CreateHttpsClient();

        using HttpResponseMessage response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = IdentityFactory.AdminEmail,
            ["Password"] = "Cualquiera!123",
        }), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Registro_CorreoExistente_RespondeIgualQueUnoNuevoYSinRol()
    {
        SkipWithoutDocker();
        string email = $"nuevo-{Guid.NewGuid():N}@secunitec.test";
        using HttpClient client = _factory.CreateHttpsClient();

        using HttpResponseMessage first = await PostRegisterAsync(client, email, UserPassword);
        using HttpResponseMessage second = await PostRegisterAsync(client, email, UserPassword);

        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal(first.StatusCode, second.StatusCode);
        Assert.Equal(first.Headers.Location, second.Headers.Location);

        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        UserManager<ApplicationUser> users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser user = await users.FindByEmailAsync(email) ?? throw new InvalidOperationException("No se creó.");
        Assert.Empty(await users.GetRolesAsync(user));
    }

    private const int StatusCodes423 = 423;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void SkipWithoutDocker() => Assert.SkipWhen(_factory.SkipReason is not null, _factory.SkipReason ?? "");

    private async Task<string> CreateUserAsync()
    {
        string email = $"usuario-{Guid.NewGuid():N}@secunitec.test";
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        await IdentitySeeder.EnsureUserAsync(
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
            email, UserPassword, IdentityFactory.TenantId, SecunitecRoles.Facturador, clienteId: null);
        return email;
    }

    private static async Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client, string email, string password, string returnUrl, Dictionary<string, string>? headers = null)
    {
        string token = await AntiforgeryTokenAsync(client, "/account/login");
        using HttpRequestMessage request = new(HttpMethod.Post, "/account/login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = email,
                ["Password"] = password,
                ["ReturnUrl"] = returnUrl,
                ["__RequestVerificationToken"] = token,
            }),
        };
        foreach ((string name, string value) in headers ?? [])
        {
            request.Headers.Add(name, value);
        }

        return await client.SendAsync(request, Ct);
    }

    private static async Task<HttpResponseMessage> PostRegisterAsync(HttpClient client, string email, string password)
    {
        string token = await AntiforgeryTokenAsync(client, "/account/register");
        return await client.PostAsync("/account/register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token,
        }), Ct);
    }

    private static async Task<string> AntiforgeryTokenAsync(HttpClient client, string page)
    {
        string html = await client.GetStringAsync(page, Ct);
        return AntiforgeryInput().Match(html).Groups[1].Value;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryInput();
}
