using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace Secunitec.Identity.Tests;

// 5.3: el conjunto de clientes de client credentials es exacto y los de carga solo existen en Development.
public sealed class JmeterClientsTests
{
    private static readonly IHostEnvironment _development = new HostingEnvironment { EnvironmentName = Environments.Development };
    private static readonly IHostEnvironment _production = new HostingEnvironment { EnvironmentName = Environments.Production };

    private static IConfiguration Config(string? clientes) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [JmeterClients.LoadClientsKey] = clientes })
        .Build();

    [Fact]
    public void LoadClientIds_SinConfigurar_NoHayClientesDeCarga()
    {
        Assert.Empty(JmeterClients.LoadClientIds(Config(null), _development));
        Assert.Empty(JmeterClients.LoadClientIds(Config(""), _development));
    }

    [Fact]
    public void LoadClientIds_EnDevelopment_NumeraDesde01()
    {
        Assert.Equal(["jmeter-load-01", "jmeter-load-02", "jmeter-load-03"], JmeterClients.LoadClientIds(Config("3"), _development));
    }

    [Fact]
    public void LoadClientIds_FueraDeDevelopment_NoHayClientesDeCarga()
    {
        Assert.Empty(JmeterClients.LoadClientIds(Config("10"), _production));
        Assert.False(JmeterClients.IsAuthorized("jmeter-load-01", Config("10"), _production));
        Assert.True(JmeterClients.IsAuthorized(ConnectEndpoints.JmeterClientId, Config("10"), _production));
    }

    [Theory]
    [InlineData("jmeter-load-04")]
    [InlineData("jmeter-load-1")]
    [InlineData("jmeter-load-")]
    [InlineData("JMETER-LOAD-01")]
    [InlineData("spa-secunitec")]
    [InlineData(null)]
    public void IsAuthorized_FueraDelConjuntoExacto_Rechaza(string? clientId)
    {
        Assert.False(JmeterClients.IsAuthorized(clientId, Config("3"), _development));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("51")]
    [InlineData("diez")]
    public void LoadClientIds_CantidadFueraDeRango_Falla(string clientes)
    {
        Assert.Throws<InvalidOperationException>(() => JmeterClients.LoadClientIds(Config(clientes), _development));
    }
}
