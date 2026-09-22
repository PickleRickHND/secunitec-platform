using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Secunitec.BuildingBlocks.Http;
using Secunitec.BuildingBlocks.Tests.Support;

namespace Secunitec.BuildingBlocks.Tests.Http;

public sealed class ProblemDetailsTests
{
    private const string Secreto = "cadena de conexion secreta";

    [Fact]
    public async Task Excepcion_EnProduccion_Devuelve500SinDetallesInternos_A05()
    {
        await using TestApp app = await TestApp.StartAsync(a =>
            a.MapGet("/boom", IResult () => throw new InvalidOperationException(Secreto)).AllowAnonymous());

        HttpResponseMessage response = await app.Client.GetAsync("/boom", TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using JsonDocument json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(Secreto, body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.False(json.RootElement.TryGetProperty("exception", out _));
        Assert.Equal(500, json.RootElement.GetProperty("status").GetInt32());

        string correlation = json.RootElement.GetProperty("correlationId").GetString()!;
        Assert.Equal(correlation, Assert.Single(response.Headers.GetValues(CorrelationId.HeaderName)));
    }

    [Fact]
    public async Task RutaInexistente_Anonimo_Responde401_R04()
    {
        // Con FallbackPolicy, sondear rutas sin token no revela cuáles existen: siempre 401.
        await using TestApp app = await TestApp.StartAsync(a => a.MapGet("/", () => Results.Ok()).AllowAnonymous());

        HttpResponseMessage response = await app.Client.GetAsync("/no-existe", TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using JsonDocument json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(json.RootElement.TryGetProperty("correlationId", out _));
    }

    [Fact]
    public async Task RutaInexistente_Autenticado_Responde404ProblemDetails()
    {
        await using TestApp app = await TestApp.StartAsync(a => a.MapGet("/", () => Results.Ok()).AllowAnonymous());
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/no-existe").AsRoles("Cliente");

        HttpResponseMessage response = await app.Client.SendAsync(request, TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using JsonDocument json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(json.RootElement.TryGetProperty("correlationId", out _));
    }
}
