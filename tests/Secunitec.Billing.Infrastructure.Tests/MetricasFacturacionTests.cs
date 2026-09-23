using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace Secunitec.Billing.Infrastructure.Tests;

// 5.2 (R19): el nombre del instrumento es el contrato con el dashboard (secunitec_billing_invoices_emitted_total).
public sealed class MetricasFacturacionTests
{
    [Fact]
    public void FacturaEmitida_SumaUnoAlContadorDelMeterDeBilling()
    {
        using TestMeterFactory meters = new();
        MetricasFacturacion metricas = new(meters);
        using MetricCollector<long> emitidas = new(meters, MetricasFacturacion.MeterName, MetricasFacturacion.InvoicesEmittedInstrument);

        metricas.FacturaEmitida();
        metricas.FacturaEmitida();

        Assert.Equal([1L, 1L], emitidas.GetMeasurementSnapshot().Select(x => x.Value));
        Assert.All(emitidas.GetMeasurementSnapshot(), x => Assert.Empty(x.Tags));
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];

        public Meter Create(MeterOptions options)
        {
            Meter meter = new(options.Name, options.Version, options.Tags, scope: this);
            _meters.Add(meter);
            return meter;
        }

        public void Dispose()
        {
            foreach (Meter meter in _meters)
            {
                meter.Dispose();
            }
        }
    }
}
