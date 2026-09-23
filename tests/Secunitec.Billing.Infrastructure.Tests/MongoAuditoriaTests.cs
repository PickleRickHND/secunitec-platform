using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using MongoDB.Driver;
using Secunitec.Billing.Domain;

namespace Secunitec.Billing.Infrastructure.Tests;

public sealed class MongoAuditoriaTests
{
    // Puerto 1 en loopback: conexión rechazada al instante, sin depender de Docker.
    private const string MongoInalcanzable = "mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=200&connectTimeoutMS=200";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Fase 4: cada evento de auditoría llega a Loki como log Information, sin actor, IP ni detalle (TB3).
    [Fact]
    public async Task Registrar_EscribeElEventoEnElLog_SinDatosPersonales()
    {
        FakeLogger<MongoAuditoria> logger = new();
        MongoAuditoria auditoria = new(new MongoClient(MongoInalcanzable).GetDatabase("secunitec_audit"), logger);
        Guid actor = Guid.NewGuid();
        Guid factura = Guid.NewGuid();

        await auditoria.Registrar(new EventoAuditoria(DateTimeOffset.UtcNow, Guid.NewGuid(), actor, AccionesAuditoria.FacturaEmitida,
            factura.ToString("D"), ResultadoAuditoria.Exito, "203.0.113.9", "corr-1", "detalle con datos del cliente"), Ct);

        FakeLogRecord evento = Assert.Single(logger.Collector.GetSnapshot(), x => x.Level == LogLevel.Information);
        Assert.Equal(2203, evento.Id.Id);
        Assert.Equal($"Auditoría: {AccionesAuditoria.FacturaEmitida} (exito) sobre {factura:D}.", evento.Message);
        Assert.DoesNotContain(logger.Collector.GetSnapshot(), x =>
            x.Message.Contains("203.0.113.9", StringComparison.Ordinal) ||
            x.Message.Contains(actor.ToString("D"), StringComparison.Ordinal) ||
            x.Message.Contains("detalle", StringComparison.Ordinal));
        // Mongo caído: la operación sigue y queda el warning de siempre.
        Assert.Contains(logger.Collector.GetSnapshot(), x => x.Level == LogLevel.Warning && x.Id.Id == 2201);
    }
}
