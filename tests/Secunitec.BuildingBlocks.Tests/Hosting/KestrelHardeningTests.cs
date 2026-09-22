using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Secunitec.BuildingBlocks.AspNetCore.Hosting;

namespace Secunitec.BuildingBlocks.Tests.Hosting;

/// <summary>
/// TestServer nunca emite <c>Server</c>, así que R06 se verifica contra Kestrel real en un puerto efímero.
/// </summary>
public sealed class KestrelHardeningTests
{
    [Fact]
    public async Task Kestrel_NoEmiteCabeceraServer_R06()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.ConfigureSecunitecKestrel();
        builder.Services.AddSecunitecDefaults();

        await using WebApplication app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.UseSecunitecDefaults();
        app.MapGet("/", () => Results.Ok()).AllowAnonymous();
        app.MapPost("/upload", async (HttpRequest req) =>
        {
            using StreamReader reader = new(req.Body);
            _ = await reader.ReadToEndAsync();
            return Results.Ok();
        }).AllowAnonymous();

        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using HttpClient client = new() { BaseAddress = new Uri(address) };

            HttpResponseMessage ok = await client.GetAsync("/", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.False(ok.Headers.Contains("Server"));
            Assert.False(ok.Headers.Contains("X-Powered-By"));
            Assert.Equal("nosniff", Assert.Single(ok.Headers.GetValues("X-Content-Type-Options")));

            // Límite de body: 1 MiB + 1 byte debe rechazarse (413) antes de llegar al handler.
            using ByteArrayContent tooBig = new(new byte[KestrelExtensions.DefaultMaxRequestBodyBytes + 1]);
            HttpResponseMessage rejected = await client.PostAsync("/upload", tooBig, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rejected.StatusCode);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }
}
