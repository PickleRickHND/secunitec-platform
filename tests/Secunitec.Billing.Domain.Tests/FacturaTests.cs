namespace Secunitec.Billing.Domain.Tests;

// Criterio de done de 1.2: ISV y totales, redondeo, correlativo y rango del CAI, fecha límite, transiciones de estado
// válidas e inválidas, líneas con cantidad o precio no positivos.
public sealed class FacturaTests
{
    private static readonly DateTimeOffset _ahora = new(2026, 9, 22, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly _hoy = new(2026, 9, 22);

    internal static ObligadoTributario Obligado(int desde = 5, int hasta = 6, DateOnly? limite = null) =>
        new(Guid.NewGuid(), new Rtn("08011999123456"), "Empresa", new Cai("AAAAAA-BBBBBB-CCCCCC-DDDDDD-EEEEEE-FF"),
            "000-001-01", desde, hasta, limite ?? new DateOnly(2026, 12, 31));

    private static Factura FacturaDe(ObligadoTributario obligado, params LineaFactura[] lineas) =>
        new(Guid.NewGuid(), obligado.Id, Guid.NewGuid(), Guid.NewGuid(), lineas, _ahora);

    private static LineaFactura Linea(decimal precio = 10, bool exento = false) => new("servicio", 1, precio, exento);

    private static string Codigo(Action accion) => Assert.Throws<BillingRuleException>(accion).Code;

    [Fact]
    public void Crear_ConLineaExenta_CalculaIsvSoloSobreGravadas()
    {
        Factura factura = FacturaDe(Obligado(),
            new LineaFactura("gravado", 2, 10.01m, false),
            new LineaFactura("exento", 1, 8.00m, true));

        Assert.Equal(28.02m, factura.Subtotal.Amount);
        Assert.Equal(3.00m, factura.Isv.Amount);
        Assert.Equal(31.02m, factura.Total.Amount);
        Assert.Equal(_ahora, factura.CreadaEn);
    }

    [Fact]
    public void Crear_ImportesConMedioCentavo_RedondeaHaciaArriba()
    {
        // 3 × 0.335 no es un precio válido (3 decimales); 0.5 × 0.05 = 0.025 → 0.03 (half away from zero).
        Factura factura = FacturaDe(Obligado(), new LineaFactura("medio centavo", 0.5m, 0.05m, false));

        Assert.Equal(0.03m, factura.Subtotal.Amount);
        // ISV: 0.03 × 0.15 = 0.0045 → 0.00; total = subtotal + isv.
        Assert.Equal(0.00m, factura.Isv.Amount);
        Assert.Equal(0.03m, factura.Total.Amount);
    }

    [Fact]
    public void Money_RedondeaAwayFromZeroYRechazaNegativos()
    {
        Assert.Equal(1.01m, Money.Of(1.005m).Amount);
        Assert.Equal(2.00m, Money.Of(1.995m).Amount);
        Assert.Equal(BillingErrorCodes.MontoInvalido, Codigo(() => Money.Of(-0.01m)));
        Assert.Equal(BillingErrorCodes.MontoInvalido, Codigo(() => Money.Of(Money.Maximo + 1)));
    }

    [Fact]
    public void Crear_SinLineas_Rechazada()
    {
        Assert.Equal(BillingErrorCodes.FacturaSinLineas, Codigo(() => FacturaDe(Obligado())));
    }

    [Fact]
    public void Crear_MasDeCienLineas_Rechazada()
    {
        LineaFactura[] lineas = Enumerable.Range(0, 101).Select(_ => Linea()).ToArray();

        Assert.Equal(BillingErrorCodes.FacturaDemasiadasLineas, Codigo(() => FacturaDe(Obligado(), lineas)));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, 0)]
    [InlineData(1, -5)]
    [InlineData(1.00001, 10)]
    [InlineData(1, 10.001)]
    public void Linea_CantidadOPrecioInvalidos_Rechazada(double cantidad, double precio)
    {
        Assert.Equal(BillingErrorCodes.LineaInvalida,
            Codigo(() => _ = new LineaFactura("x", (decimal)cantidad, (decimal)precio, false)));
    }

    [Fact]
    public void Emitir_DosFacturas_YRangoAgotado_NoReutilizaNumeros()
    {
        ObligadoTributario obligado = Obligado();
        Factura primera = FacturaDe(obligado, Linea());
        Factura segunda = FacturaDe(obligado, Linea());
        Factura tercera = FacturaDe(obligado, Linea());

        primera.Emitir(obligado, _hoy);
        segunda.Emitir(obligado, _hoy);

        Assert.Equal("000-001-01-00000005", primera.Numero);
        Assert.Equal("000-001-01-00000006", segunda.Numero);
        Assert.Equal(obligado.Cai, primera.Cai);
        Assert.Equal(_hoy, primera.FechaEmision);
        Assert.Equal(BillingErrorCodes.RangoAgotado, Codigo(() => tercera.Emitir(obligado, _hoy)));
    }

    [Fact]
    public void Emitir_DosVeces_Rechazada()
    {
        ObligadoTributario obligado = Obligado();
        Factura factura = FacturaDe(obligado, Linea());
        factura.Emitir(obligado, _hoy);

        Assert.Equal(BillingErrorCodes.FacturaEstadoInvalido, Codigo(() => factura.Emitir(obligado, _hoy)));
        Assert.Equal(6, obligado.SiguienteCorrelativo);
    }

    [Fact]
    public void Emitir_CaiVencido_NoAvanzaCorrelativo()
    {
        ObligadoTributario obligado = Obligado(limite: new DateOnly(2026, 9, 21));
        Factura factura = FacturaDe(obligado, Linea());

        Assert.Equal(BillingErrorCodes.CaiVencido, Codigo(() => factura.Emitir(obligado, _hoy)));
        Assert.Equal(5, obligado.SiguienteCorrelativo);
        Assert.Equal(EstadoFactura.Borrador, factura.Estado);
    }

    [Fact]
    public void Emitir_EnLaFechaLimite_Permitido()
    {
        ObligadoTributario obligado = Obligado(limite: _hoy);
        Factura factura = FacturaDe(obligado, Linea());

        factura.Emitir(obligado, _hoy);

        Assert.Equal(EstadoFactura.Emitida, factura.Estado);
    }

    [Fact]
    public void Emitir_ObligadoInactivo_Rechazada()
    {
        ObligadoTributario obligado = Obligado();
        obligado.Desactivar();

        Assert.Equal(BillingErrorCodes.ObligadoInactivo, Codigo(() => FacturaDe(obligado, Linea()).Emitir(obligado, _hoy)));
    }

    [Fact]
    public void Emitir_ConObligadoAjeno_Rechazada()
    {
        Factura factura = FacturaDe(Obligado(), Linea());

        Assert.Equal(BillingErrorCodes.FacturaEstadoInvalido, Codigo(() => factura.Emitir(Obligado(), _hoy)));
    }

    [Fact]
    public void Anular_SoloEmitida_ConMotivo_EstadoIrreversible()
    {
        ObligadoTributario obligado = Obligado();
        Factura factura = FacturaDe(obligado, Linea());
        Assert.Equal(BillingErrorCodes.FacturaEstadoInvalido, Codigo(() => factura.Anular("error de digitación")));

        factura.Emitir(obligado, _hoy);
        factura.Anular("  Error de digitación  ");

        Assert.Equal(EstadoFactura.Anulada, factura.Estado);
        Assert.Equal("Error de digitación", factura.MotivoAnulacion);
        Assert.Equal(BillingErrorCodes.FacturaEstadoInvalido, Codigo(() => factura.Anular("otra vez")));
        Assert.Equal(BillingErrorCodes.FacturaEstadoInvalido, Codigo(() => factura.Emitir(obligado, _hoy)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Anular_SinMotivo_Rechazada(string motivo)
    {
        ObligadoTributario obligado = Obligado();
        Factura factura = FacturaDe(obligado, Linea());
        factura.Emitir(obligado, _hoy);

        Assert.Equal(BillingErrorCodes.MotivoRequerido, Codigo(() => factura.Anular(motivo)));
        Assert.Equal(BillingErrorCodes.MotivoRequerido, Codigo(() => factura.Anular(new string('m', 251))));
        Assert.Equal(EstadoFactura.Emitida, factura.Estado);
    }

    [Fact]
    public void Lineas_EnBorrador_SeAgreganYQuitanYRecalculan()
    {
        Factura factura = FacturaDe(Obligado(), Linea(100));

        factura.AgregarLinea(new LineaFactura("exento", 2, 50, true));

        Assert.Equal(200.00m, factura.Subtotal.Amount);
        Assert.Equal(15.00m, factura.Isv.Amount);
        Assert.Equal(215.00m, factura.Total.Amount);
        Assert.Equal(2, factura.Lineas.Count);
    }

    [Fact]
    public void Lineas_QuitarLaUnica_Rechazado()
    {
        Factura factura = FacturaDe(Obligado(), Linea());

        Assert.Equal(BillingErrorCodes.FacturaUltimaLinea, Codigo(() => factura.QuitarLinea(0)));
        Assert.Equal(BillingErrorCodes.LineaNoEncontrada, Codigo(() => factura.QuitarLinea(99)));
    }

    [Fact]
    public void Lineas_EnFacturaEmitida_NoSeModifican()
    {
        ObligadoTributario obligado = Obligado();
        Factura factura = FacturaDe(obligado, Linea(), Linea());
        factura.Emitir(obligado, _hoy);

        Assert.Equal(BillingErrorCodes.FacturaEstadoInvalido, Codigo(() => factura.AgregarLinea(Linea())));
        Assert.Equal(BillingErrorCodes.FacturaEstadoInvalido, Codigo(() => factura.QuitarLinea(0)));
    }
}
