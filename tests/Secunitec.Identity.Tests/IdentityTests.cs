using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using MongoDB.Bson;
using MongoDB.Driver;
using OpenIddict.Abstractions;
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

    // 5.3: cada cliente de carga tiene su propio sub (su partición en el rate limiting) y escribe en el tenant de carga.
    [Fact]
    public async Task ClientCredentials_ClienteDeCarga_TieneSuPropioSubYElTenantDeCarga()
    {
        SkipWithoutDocker();
        using HttpClient client = _factory.CreateHttpsClient();

        JsonWebToken principal = await ClientCredentialsToken(client, ConnectEndpoints.JmeterClientId);
        JsonWebToken carga2 = await ClientCredentialsToken(client, "jmeter-load-02");
        JsonWebToken carga3 = await ClientCredentialsToken(client, "jmeter-load-03");

        Assert.Equal(3, new[] { principal.Subject, carga2.Subject, carga3.Subject }.Distinct().Count());
        Assert.Equal(IdentityFactory.TenantId.ToString(), principal.GetClaim(SecunitecClaims.TenantId).Value);
        Assert.Equal(IdentityFactory.LoadTenantId.ToString(), carga2.GetClaim(SecunitecClaims.TenantId).Value);
        Assert.Equal("jmeter-load-02", carga2.GetClaim(SecunitecClaims.ClientId).Value);
        Assert.Equal(SecunitecRoles.Facturador, carga3.GetClaim(SecunitecClaims.Role).Value);
    }

    [Fact]
    public async Task ClientCredentials_ClienteDeCargaNoConfigurado_NoRecibeToken()
    {
        SkipWithoutDocker();
        using HttpClient client = _factory.CreateHttpsClient();

        using HttpResponseMessage response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "jmeter-load-99",
            ["client_secret"] = IdentityFactory.JmeterSecret,
            ["scope"] = SecunitecAudiences.Billing,
        }), Ct);

        // OpenIddict rechaza al cliente antes de llegar a ConnectEndpoints: no existe una aplicación con ese id.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("invalid_client", body, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", body, StringComparison.Ordinal);
    }

    private static async Task<JsonWebToken> ClientCredentialsToken(HttpClient client, string clientId)
    {
        using HttpResponseMessage response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = IdentityFactory.JmeterSecret,
            ["scope"] = SecunitecAudiences.Billing,
        }), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return new JsonWebToken(body.RootElement.GetProperty("access_token").GetString());
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
        // Billing lee estos eventos para la pantalla de auditoría: fecha BSON (ordenable y filtrable) y tenant UUID.
        Assert.Equal(BsonType.DateTime, audit["timestamp"].BsonType);
        Assert.Equal(IdentityFactory.TenantId, audit["tenantId"].AsGuid);
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
    [InlineData("/\\evil.example", "http://localhost:3000/")]
    [InlineData("//evil.example", "http://localhost:3000/")]
    [InlineData("https://evil.example/", "http://localhost:3000/")]
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

    [Fact]
    public async Task Discovery_AnunciaElCierreDeSesion()
    {
        SkipWithoutDocker();
        using HttpClient client = _factory.CreateHttpsClient();

        using JsonDocument discovery = JsonDocument.Parse(await client.GetStringAsync("/.well-known/openid-configuration", Ct));

        Assert.Equal(IdentityFactory.Issuer + "connect/endsession", discovery.RootElement.GetProperty("end_session_endpoint").GetString());
    }

    [Fact]
    public async Task CodigoPkce_DelSpa_EmiteIdTokenConRolYTenant()
    {
        SkipWithoutDocker();
        string email = await CreateUserAsync();
        using HttpClient client = _factory.CreateHttpsClient();
        using HttpResponseMessage login = await PostLoginAsync(client, email, UserPassword, "/");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        string verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        using HttpResponseMessage authorize = await client.GetAsync(
            "/connect/authorize?client_id=spa-secunitec&response_type=code&state=estado-1" +
            "&redirect_uri=" + Uri.EscapeDataString(SpaCallback) +
            "&scope=" + Uri.EscapeDataString("openid profile email roles offline_access secunitec-billing") +
            "&code_challenge=" + challenge + "&code_challenge_method=S256", Ct);

        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        Uri callback = authorize.Headers.Location ?? throw new InvalidOperationException("Sin redirección.");
        Assert.StartsWith(SpaCallback, callback.ToString(), StringComparison.Ordinal);
        string code = QueryValue(callback, "code");

        using HttpResponseMessage token = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = IdentitySeeder.SpaClientId,
            ["code"] = code,
            ["redirect_uri"] = SpaCallback,
            ["code_verifier"] = verifier,
        }), Ct);

        Assert.Equal(HttpStatusCode.OK, token.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await token.Content.ReadAsStringAsync(Ct));
        Assert.True(body.RootElement.TryGetProperty("refresh_token", out _));
        JsonWebToken idToken = new(body.RootElement.GetProperty("id_token").GetString());
        Assert.Equal(SecunitecRoles.Facturador, idToken.GetClaim(SecunitecClaims.Role).Value);
        Assert.Equal(IdentityFactory.TenantId.ToString(), idToken.GetClaim(SecunitecClaims.TenantId).Value);
        JsonWebToken accessToken = new(body.RootElement.GetProperty("access_token").GetString());
        Assert.Contains(SecunitecAudiences.Billing, accessToken.Audiences);
    }

    [Fact]
    public async Task Authorize_PromptNoneSinSesion_DevuelveLoginRequired()
    {
        SkipWithoutDocker();
        using HttpClient client = _factory.CreateHttpsClient();
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(Base64Url(RandomNumberGenerator.GetBytes(32)))));

        using HttpResponseMessage authorize = await client.GetAsync(
            "/connect/authorize?client_id=spa-secunitec&response_type=code&scope=openid&prompt=none&state=s" +
            "&redirect_uri=" + Uri.EscapeDataString(SpaCallback) + "&code_challenge=" + challenge + "&code_challenge_method=S256", Ct);

        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        Assert.Equal("login_required", QueryValue(authorize.Headers.Location!, "error"));
    }

    [Fact]
    public async Task CierreDeSesion_BorraLaCookieYVuelveAlSpa()
    {
        SkipWithoutDocker();
        string email = await CreateUserAsync();
        using HttpClient client = _factory.CreateHttpsClient();
        using HttpResponseMessage login = await PostLoginAsync(client, email, UserPassword, "/");

        using HttpResponseMessage endSession = await client.GetAsync(
            "/connect/endsession?client_id=spa-secunitec&post_logout_redirect_uri=" + Uri.EscapeDataString(SpaLogoutCallback), Ct);

        Assert.Equal(HttpStatusCode.Redirect, endSession.StatusCode);
        Assert.Equal(SpaLogoutCallback, endSession.Headers.Location?.GetLeftPart(UriPartial.Path));
        string cookie = endSession.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-Secunitec.Identity=", StringComparison.Ordinal));
        Assert.Contains("expires=Thu, 01 Jan 1970", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Seeder_SincronizaUnClienteSpaCreadoAntesDeLaEtapa4()
    {
        SkipWithoutDocker();
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        IOpenIddictApplicationManager applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        IConfiguration configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        // Simula el cliente de una base de la etapa 3: sin cierre de sesión ni URI de vuelta.
        OpenIddictApplicationDescriptor viejo = IdentitySeeder.SpaDescriptor(configuration);
        viejo.PostLogoutRedirectUris.Clear();
        viejo.Permissions.Remove(OpenIddictConstants.Permissions.Endpoints.EndSession);
        await applications.UpdateAsync(await applications.FindByClientIdAsync(IdentitySeeder.SpaClientId, Ct) ?? throw new InvalidOperationException(), viejo, Ct);

        await IdentitySeeder.SeedAsync(_factory.Services, configuration);

        // Otro scope: el DbContext de este conserva la entidad vieja en su change tracker.
        await using AsyncServiceScope check = _factory.Services.CreateAsyncScope();
        IOpenIddictApplicationManager fresh = check.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        object spa = await fresh.FindByClientIdAsync(IdentitySeeder.SpaClientId, Ct) ?? throw new InvalidOperationException();
        Assert.Contains(OpenIddictConstants.Permissions.Endpoints.EndSession, await fresh.GetPermissionsAsync(spa, Ct));
        Assert.Contains(SpaLogoutCallback, await fresh.GetPostLogoutRedirectUrisAsync(spa, Ct));
    }

    [Fact]
    public async Task EstilosDeCuenta_SonPublicosYConservanLaCspDeIdentity()
    {
        SkipWithoutDocker();
        using HttpClient client = _factory.CreateHttpsClient();

        using HttpResponseMessage css = await client.GetAsync("/account/assets/identity.css", Ct);
        using HttpResponseMessage page = await client.GetAsync("/account/login", Ct);

        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.Equal("text/css", css.Content.Headers.ContentType?.MediaType);
        Assert.Contains("href=\"/account/assets/identity.css\"", await page.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.StartsWith("default-src 'self'", page.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    private const string SpaCallback = "http://localhost:3000/auth/callback";
    private const string SpaLogoutCallback = "http://localhost:3000/auth/logout-callback";

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string QueryValue(Uri uri, string name) =>
        System.Web.HttpUtility.ParseQueryString(uri.Query)[name] ?? throw new InvalidOperationException($"Falta {name} en {uri}.");

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
