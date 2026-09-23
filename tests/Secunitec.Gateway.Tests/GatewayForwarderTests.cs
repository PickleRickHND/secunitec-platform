using Microsoft.Extensions.DependencyInjection;
using Secunitec.BuildingBlocks.AspNetCore.Hosting;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace Secunitec.Gateway.Tests;

// Regresión de la etapa 5.3: 3 respuestas 502 en la rampa de JMeter porque el pool de YARP y el keep-alive de Kestrel
// de Billing duraban lo mismo (60 s) y la ventana fija del rate limiting los alineaba.
public sealed class GatewayForwarderTests : IClassFixture<GatewayFactory>
{
    private readonly GatewayFactory _factory;

    public GatewayForwarderTests(GatewayFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Gateway_UsaLaFabricaDeYarpDelProyecto()
    {
        Assert.IsType<GatewayForwarderHttpClientFactory>(_factory.Services.GetRequiredService<IForwarderHttpClientFactory>());
    }

    [Fact]
    public void PoolDeYarp_SueltaLasConexionesAntesDelKeepAliveDeLosServiciosInternos()
    {
        using SocketsHttpHandler handler = new SondaDeFabrica().Configurar();

        Assert.Equal(GatewayForwarderHttpClientFactory.PooledConnectionIdleTimeout, handler.PooledConnectionIdleTimeout);
        Assert.True(handler.PooledConnectionIdleTimeout < KestrelExtensions.KeepAliveTimeout);
    }

    private sealed class SondaDeFabrica : GatewayForwarderHttpClientFactory
    {
        public SocketsHttpHandler Configurar()
        {
            SocketsHttpHandler handler = new();
            ConfigureHandler(new ForwarderHttpClientContext { ClusterId = "billing", NewConfig = HttpClientConfig.Empty }, handler);
            return handler;
        }
    }
}
