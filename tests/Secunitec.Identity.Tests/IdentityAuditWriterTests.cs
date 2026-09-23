using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using MongoDB.Driver;

namespace Secunitec.Identity.Tests;

public sealed class IdentityAuditWriterTests
{
    // Puerto 1 en loopback: conexión rechazada al instante, sin depender de Docker.
    private const string MongoInalcanzable = "mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=200&connectTimeoutMS=200";

    // Fase 4: cada evento de autenticación llega a Loki como log Information, sin actor, IP ni detalle (TB3).
    [Fact]
    public async Task WriteAsync_EscribeElEventoEnElLog_SinDatosPersonales()
    {
        FakeLogger<IdentityAuditWriter> logger = new();
        IdentityAuditWriter writer = new(new MongoClient(MongoInalcanzable).GetDatabase("secunitec_audit"), logger);
        Guid actor = Guid.NewGuid();
        DefaultHttpContext context = new();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.9");

        await writer.WriteAsync(context, "login", success: false, actor, Guid.NewGuid(), "invalid_credentials",
            TestContext.Current.CancellationToken);

        FakeLogRecord evento = Assert.Single(logger.Collector.GetSnapshot(), x => x.Level == LogLevel.Information);
        Assert.Equal(3102, evento.Id.Id);
        Assert.Equal("Auditoría: login (fallido).", evento.Message);
        Assert.DoesNotContain(logger.Collector.GetSnapshot(), x =>
            x.Message.Contains("203.0.113.9", StringComparison.Ordinal) ||
            x.Message.Contains(actor.ToString("D"), StringComparison.Ordinal) ||
            x.Message.Contains("invalid_credentials", StringComparison.Ordinal));
        // Mongo caído: el login sigue y queda el warning de siempre.
        Assert.Contains(logger.Collector.GetSnapshot(), x => x.Level == LogLevel.Warning && x.Id.Id == 3101);
    }
}
