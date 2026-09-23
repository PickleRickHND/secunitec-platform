namespace Secunitec.Billing.Domain.Tests;

public sealed class ObligadoTests
{
    private static readonly DateOnly _hoy = new(2026, 9, 22);

    [Fact]
    public void ActualizarCai_ReiniciaElCorrelativoAlNuevoRango()
    {
        ObligadoTributario obligado = FacturaTests.Obligado(desde: 1, hasta: 1);
        obligado.ReservarNumero(_hoy);
        Assert.Throws<BillingRuleException>(() => obligado.ReservarNumero(_hoy));

        obligado.ActualizarCai(new Cai("ZZZZZZ-YYYYYY-XXXXXX-WWWWWW-VVVVVV-UU"), "000-001-01", 101, 200, new DateOnly(2027, 6, 30));

        Assert.Equal("000-001-01-00000101", obligado.ReservarNumero(_hoy));
        Assert.Equal("ZZZZZZ-YYYYYY-XXXXXX-WWWWWW-VVVVVV-UU", obligado.Cai.Value);
    }

    [Theory]
    [InlineData("000-001-1", 1, 10)]
    [InlineData("000-001-01", 0, 10)]
    [InlineData("000-001-01", 10, 9)]
    [InlineData("000-001-01", 1, 100_000_000)]
    public void Crear_RangoOPrefijoInvalido_Rechazado(string prefijo, int desde, int hasta) =>
        Assert.Equal(BillingErrorCodes.RangoInvalido, Assert.Throws<BillingRuleException>(() => new ObligadoTributario(Guid.NewGuid(),
            new Rtn("08011999123456"), "Empresa", new Cai("AAAAAA-BBBBBB-CCCCCC-DDDDDD-EEEEEE-FF"), prefijo, desde, hasta,
            _hoy)).Code);

    [Fact]
    public void DesactivarYActivar_ControlanLaEmision()
    {
        ObligadoTributario obligado = FacturaTests.Obligado();
        obligado.Desactivar();
        Assert.Equal(BillingErrorCodes.ObligadoInactivo, Assert.Throws<BillingRuleException>(() => obligado.ReservarNumero(_hoy)).Code);

        obligado.Activar();

        Assert.Equal("000-001-01-00000005", obligado.ReservarNumero(_hoy));
    }
}
