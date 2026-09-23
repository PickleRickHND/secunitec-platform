// R18 (etapa 5.3): sin 502 por conexiones que el servicio interno ya cerró.
// En la rampa de JMeter, 3 de 877 348 peticiones dieron 502 ("The response ended prematurely"). La ventana fija de 60 s
// del rate limiting deja a Billing sin tráfico unos 60 s entre ráfagas, justo el keep-alive de Kestrel (60 s) y la vida
// por defecto de una conexión inactiva en el pool de YARP (60 s): YARP reusaba conexiones que Billing estaba cerrando.
// Regla: el cliente suelta sus conexiones inactivas antes de que el servidor las cierre.

using Secunitec.BuildingBlocks.AspNetCore.Hosting;
using Yarp.ReverseProxy.Forwarder;

namespace Secunitec.Gateway;

public class GatewayForwarderHttpClientFactory : ForwarderHttpClientFactory
{
    /// <summary>La mitad del keep-alive de los servicios internos (<see cref="KestrelExtensions.KeepAliveTimeout"/>).</summary>
    public static readonly TimeSpan PooledConnectionIdleTimeout = KestrelExtensions.KeepAliveTimeout / 2;

    protected override void ConfigureHandler(ForwarderHttpClientContext context, SocketsHttpHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        base.ConfigureHandler(context, handler);
        handler.PooledConnectionIdleTimeout = PooledConnectionIdleTimeout;
    }
}
