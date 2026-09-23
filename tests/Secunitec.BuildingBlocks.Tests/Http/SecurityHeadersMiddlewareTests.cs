using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Secunitec.BuildingBlocks.Tests.Support;

namespace Secunitec.BuildingBlocks.Tests.Http;

public sealed class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task Respuesta_TraeLasCabecerasDeSeguridadPorDefecto()
    {
        await using TestApp app = await TestApp.StartAsync(a => a.MapGet("/", () => Results.Ok(new { ok = true })).AllowAnonymous());

        HttpResponseMessage response = await app.Client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Contains("camera=()", Header(response, "Permissions-Policy"), StringComparison.Ordinal);
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", Header(response, "Content-Security-Policy"));
        Assert.Equal("no-store", Header(response, "Cache-Control"));
    }

    [Fact]
    public async Task Respuesta_NoRevelaTecnologia_R06()
    {
        await using TestApp app = await TestApp.StartAsync(a => a.MapGet("/", (HttpContext ctx) =>
        {
            // Simula un componente que intenta anunciar el stack: el middleware lo elimina al iniciar la respuesta.
            ctx.Response.Headers["X-Powered-By"] = "ASP.NET";
            ctx.Response.Headers["Server"] = "Kestrel";
            return Results.Ok();
        }).AllowAnonymous());

        HttpResponseMessage response = await app.Client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("X-Powered-By"));
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task Csp_EsConfigurablePorServicio()
    {
        await using TestApp app = await TestApp.StartAsync(
            a => a.MapGet("/", () => Results.Ok()).AllowAnonymous(),
            headers: o => o.ContentSecurityPolicy = "default-src 'self'");

        HttpResponseMessage response = await app.Client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal("default-src 'self'", Header(response, "Content-Security-Policy"));
    }

    [Fact]
    public async Task Csp_NoPisaLaQueYaTraeLaRespuesta()
    {
        // Caso del gateway: la respuesta reenviada de Identity trae su propia CSP.
        await using TestApp app = await TestApp.StartAsync(a => a.MapGet("/", (HttpContext ctx) =>
        {
            ctx.Response.Headers.ContentSecurityPolicy = "default-src 'self'; frame-ancestors 'none'";
            return Results.Ok();
        }).AllowAnonymous());

        HttpResponseMessage response = await app.Client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal("default-src 'self'; frame-ancestors 'none'", Header(response, "Content-Security-Policy"));
    }

    [Fact]
    public async Task CacheControl_NoPisaUnValorExplicitoDelEndpoint()
    {
        await using TestApp app = await TestApp.StartAsync(a => a.MapGet("/", (HttpContext ctx) =>
        {
            ctx.Response.Headers.CacheControl = "public, max-age=60";
            return Results.Ok();
        }).AllowAnonymous());

        HttpResponseMessage response = await app.Client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal("public, max-age=60", Header(response, "Cache-Control"));
    }

    [Fact]
    public async Task Endpoint_SinAllowAnonymous_Responde401_R04()
    {
        await using TestApp app = await TestApp.StartAsync(a => a.MapGet("/protegido", () => Results.Ok()));

        HttpResponseMessage anonimo = await app.Client.GetAsync("/protegido", TestContext.Current.CancellationToken);
        using HttpRequestMessage conToken = new HttpRequestMessage(HttpMethod.Get, "/protegido").AsRoles("Cliente");
        HttpResponseMessage autenticado = await app.Client.SendAsync(conToken, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
        Assert.Equal(HttpStatusCode.OK, autenticado.StatusCode);
    }

    [Fact]
    public async Task Endpoint_ConPolitica_Responde403SiElRolNoAlcanza()
    {
        await using TestApp app = await TestApp.StartAsync(a =>
            a.MapGet("/auditoria", () => Results.Ok()).RequireAuthorization(Secunitec.BuildingBlocks.Security.SecunitecPolicies.ReadAudit));

        using HttpRequestMessage cliente = new HttpRequestMessage(HttpMethod.Get, "/auditoria").AsRoles("Cliente");
        using HttpRequestMessage auditor = new HttpRequestMessage(HttpMethod.Get, "/auditoria").AsRoles("Auditor");
        HttpResponseMessage prohibido = await app.Client.SendAsync(cliente, TestContext.Current.CancellationToken);
        HttpResponseMessage permitido = await app.Client.SendAsync(auditor, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, prohibido.StatusCode);
        Assert.Equal(HttpStatusCode.OK, permitido.StatusCode);
    }

    private static string Header(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out IEnumerable<string>? values))
        {
            return Assert.Single(values);
        }

        return Assert.Single(response.Content.Headers.GetValues(name));
    }
}
