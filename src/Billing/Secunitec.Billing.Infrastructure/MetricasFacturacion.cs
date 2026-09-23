using System.Diagnostics.Metrics;
using Secunitec.Billing.Application;

namespace Secunitec.Billing.Infrastructure;

/// <summary>
/// R19 (5.2): métricas de negocio de Billing. En Prometheus: <c>secunitec_billing_invoices_emitted_total</c>.
/// Sin etiquetas de tenant ni de cliente (TB3: sin datos del negocio en la telemetría).
/// </summary>
public sealed class MetricasFacturacion : IMetricasFacturacion
{
    public const string MeterName = "Secunitec.Billing";
    public const string InvoicesEmittedInstrument = "secunitec.billing.invoices_emitted";

    private readonly Counter<long> _emitidas;

    public MetricasFacturacion(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _emitidas = meterFactory.Create(MeterName)
            .CreateCounter<long>(InvoicesEmittedInstrument, unit: "{invoice}", description: "Facturas emitidas (correlativo asignado)");
    }

    public void FacturaEmitida() => _emitidas.Add(1);
}
