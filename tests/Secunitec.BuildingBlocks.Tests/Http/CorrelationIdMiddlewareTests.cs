using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Secunitec.BuildingBlocks.AspNetCore.Http;
using Secunitec.BuildingBlocks.Http;
using Secunitec.BuildingBlocks.Tests.Support;

namespace Secunitec.BuildingBlocks.Tests.Http;

public sealed class CorrelationIdMiddlewareTests
{
    private static Task<TestApp> StartAsync() => TestApp.StartAsync(app =>
        app.MapGet("/echo", (HttpContext ctx) => Results.Text(ctx.GetCorrelationId() ?? "")).AllowAnonymous());

    [Fact]
    public async Task SinCabecera_GeneraUnaYLaDevuelve()
    {
        await using TestApp app = await StartAsync();

        HttpResponseMessage response = await app.Client.GetAsync("/echo", TestContext.Current.CancellationToken);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        string header = Assert.Single(response.Headers.GetValues(CorrelationId.HeaderName));

        Assert.True(CorrelationId.IsValid(header));
        Assert.Equal(header, body);
        Assert.Equal(32, header.Length);
    }

    [Fact]
    public async Task ConCabeceraValida_LaRespeta()
    {
        await using TestApp app = await StartAsync();
        using HttpRequestMessage request = new(HttpMethod.Get, "/echo");
        request.Headers.Add(CorrelationId.HeaderName, "gw-abc.123_XYZ");

        HttpResponseMessage response = await app.Client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("gw-abc.123_XYZ", Assert.Single(response.Headers.GetValues(CorrelationId.HeaderName)));
        Assert.Equal("gw-abc.123_XYZ", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("con espacios")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("a;b")]
    public async Task ConCabeceraInvalida_LaReemplaza(string incoming)
    {
        await using TestApp app = await StartAsync();
        using HttpRequestMessage request = new(HttpMethod.Get, "/echo");
        request.Headers.TryAddWithoutValidation(CorrelationId.HeaderName, incoming);

        HttpResponseMessage response = await app.Client.SendAsync(request, TestContext.Current.CancellationToken);
        string header = Assert.Single(response.Headers.GetValues(CorrelationId.HeaderName));

        Assert.NotEqual(incoming, header);
        Assert.True(CorrelationId.IsValid(header));
    }

    [Fact]
    public async Task ConCabeceraDemasiadoLarga_LaReemplaza()
    {
        await using TestApp app = await StartAsync();
        string tooLong = new('a', CorrelationId.MaxLength + 1);
        using HttpRequestMessage request = new(HttpMethod.Get, "/echo");
        request.Headers.Add(CorrelationId.HeaderName, tooLong);

        HttpResponseMessage response = await app.Client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.NotEqual(tooLong, Assert.Single(response.Headers.GetValues(CorrelationId.HeaderName)));
    }

    [Fact]
    public void IsValid_AceptaSoloElAlfabetoDelContrato()
    {
        Assert.True(CorrelationId.IsValid("abc-DEF_123.x"));
        Assert.True(CorrelationId.IsValid(new string('z', CorrelationId.MaxLength)));
        Assert.False(CorrelationId.IsValid(null));
        Assert.False(CorrelationId.IsValid(""));
        Assert.False(CorrelationId.IsValid("ñ"));
        Assert.False(CorrelationId.IsValid("a/b"));
        Assert.False(CorrelationId.IsValid(new string('z', CorrelationId.MaxLength + 1)));
    }
}
