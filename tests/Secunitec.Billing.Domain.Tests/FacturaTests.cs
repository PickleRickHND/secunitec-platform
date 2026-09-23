using Secunitec.Billing.Domain;

namespace Secunitec.Billing.Domain.Tests;

public sealed class FacturaTests
{
    private static ObligadoTributario Obligado(int desde = 5, int hasta = 6, DateOnly? limite = null) =>
        new(Guid.NewGuid(), "08011999123456", "Empresa", "AAAAAA-BBBBBB-CCCCCC-DDDDDD-EEEEEE-FF",
            "000-001-01", desde, hasta, limite ?? new DateOnly(2026, 12, 31));

    private static Factura FacturaDe(ObligadoTributario obligado, params LineaFactura[] lineas) =>
        new(Guid.NewGuid(), obligado.Id, Guid.NewGuid(), Guid.NewGuid(), lineas);

    [Fact]
    public void CrearFactura_ConLineaExenta_CalculaIsvSoloSobreGravadas()
    {
        ObligadoTributario obligado = Obligado();
        Factura factura = FacturaDe(obligado,
            new LineaFactura("gravado", 2, 10.01m, false),
            new LineaFactura("exento", 1, 8.00m, true));

        Assert.Equal(28.02m, factura.Subtotal);
        Assert.Equal(3.00m, factura.Isv);
        Assert.Equal(31.02m, factura.Total);
    }

    [Fact]
    public void Emitir_DosFacturas_YRangoAgotado_NoReutilizaNumeros()
    {
        ObligadoTributario obligado = Obligado();
        Factura primera = FacturaDe(obligado, new LineaFactura("a", 1, 10, false));
        Factura segunda = FacturaDe(obligado, new LineaFactura("b", 1, 10, false));
        Factura tercera = FacturaDe(obligado, new LineaFactura("c", 1, 10, false));

        primera.Emitir(obligado, new DateOnly(2026, 9, 22));
        segunda.Emitir(obligado, new DateOnly(2026, 9, 22));

        Assert.Equal("000-001-01-00000005", primera.Numero);
        Assert.Equal("000-001-01-00000006", segunda.Numero);
        Assert.Throws<BillingRuleException>(() => tercera.Emitir(obligado, new DateOnly(2026, 9, 22)));
    }

    [Fact]
    public void Emitir_CaiVencido_NoAvanzaCorrelativo()
    {
        ObligadoTributario obligado = Obligado(limite: new DateOnly(2026, 9, 21));
        Factura factura = FacturaDe(obligado, new LineaFactura("a", 1, 10, false));

        Assert.Throws<BillingRuleException>(() => factura.Emitir(obligado, new DateOnly(2026, 9, 22)));
        Assert.Equal(5, obligado.SiguienteCorrelativo);
    }

    [Fact]
    public void Anular_SoloEmitida_EstadoIrreversible()
    {
        ObligadoTributario obligado = Obligado();
        Factura factura = FacturaDe(obligado, new LineaFactura("a", 1, 10, false));
        Assert.Throws<BillingRuleException>(() => factura.Anular());
        factura.Emitir(obligado, new DateOnly(2026, 9, 22));
        factura.Anular();
        Assert.Equal(EstadoFactura.Anulada, factura.Estado);
        Assert.Throws<BillingRuleException>(() => factura.Anular());
        Assert.Throws<BillingRuleException>(() => factura.Emitir(obligado, new DateOnly(2026, 9, 22)));
    }

    [Fact]
    public void Rtn_YPrecioInvalidos_Rechazados()
    {
        Assert.Throws<BillingRuleException>(() => TaxIdentity.Rtn("123"));
        Assert.Throws<BillingRuleException>(() => new LineaFactura("x", 1, -1, false));
    }
}
