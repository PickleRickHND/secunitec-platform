using System.Net;
using Secunitec.BuildingBlocks.AspNetCore.Security;

namespace Secunitec.BuildingBlocks.Tests.Security;

public sealed class InternalAuthorityHandlerTests
{
    private static readonly Uri _publicAuthority = new("https://localhost:8080/");
    private static readonly Uri _internalAuthority = new("http://identity:8080/");

    [Theory]
    [InlineData("https://localhost:8080/.well-known/openid-configuration", "http://identity:8080/.well-known/openid-configuration")]
    [InlineData("https://localhost:8080/.well-known/jwks", "http://identity:8080/.well-known/jwks")]
    [InlineData("https://LOCALHOST:8080/.well-known/jwks?x=1", "http://identity:8080/.well-known/jwks?x=1")]
    public async Task SendAsync_UrlPublicaDeIdentity_SeEnviaALaDireccionInterna(string requested, string expected)
    {
        Uri? sent = await SendThroughHandler(new Uri(requested));

        Assert.Equal(new Uri(expected), sent);
    }

    [Theory]
    [InlineData("https://localhost:8081/.well-known/jwks")]
    [InlineData("https://evil.example/.well-known/jwks")]
    [InlineData("http://localhost:8080/.well-known/jwks")]
    public async Task SendAsync_OtraUrl_NoSeReescribe(string requested)
    {
        Uri? sent = await SendThroughHandler(new Uri(requested));

        Assert.Equal(new Uri(requested), sent);
    }

    private static async Task<Uri?> SendThroughHandler(Uri requested)
    {
        using RecordingHandler recorder = new();
        using InternalAuthorityHandler handler = new(_publicAuthority, _internalAuthority, recorder);
        using HttpClient client = new(handler, disposeHandler: false);

        using HttpResponseMessage response = await client.GetAsync(requested, TestContext.Current.CancellationToken);

        return recorder.LastRequestUri;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
