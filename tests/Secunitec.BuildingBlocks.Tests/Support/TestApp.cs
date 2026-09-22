using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Secunitec.BuildingBlocks.AspNetCore.Hosting;
using Secunitec.BuildingBlocks.AspNetCore.Http;

namespace Secunitec.BuildingBlocks.Tests.Support;

/// <summary>
/// Levanta una <see cref="WebApplication"/> mínima en memoria (TestServer) con los defaults de Secunitec
/// y el esquema de autenticación de prueba. Cada test mapea sus propios endpoints.
/// </summary>
internal sealed class TestApp : IAsyncDisposable
{
    private readonly WebApplication _app;

    private TestApp(WebApplication app)
    {
        _app = app;
    }

    public HttpClient Client { get; private set; } = null!;

    public static async Task<TestApp> StartAsync(
        Action<IEndpointRouteBuilder> map,
        string environment = "Production",
        Action<SecurityHeadersOptions>? headers = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment,
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddSecunitecDefaults(headers);
        builder.Services.AddTestAuthentication();

        WebApplication app = builder.Build();
        app.UseSecunitecDefaults();
        app.UseAuthentication();
        app.UseAuthorization();
        map(app);

        await app.StartAsync(TestContext.Current.CancellationToken);

        TestApp instance = new(app)
        {
            Client = app.GetTestClient(),
        };
        return instance;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync(TestContext.Current.CancellationToken);
        await _app.DisposeAsync();
    }
}
