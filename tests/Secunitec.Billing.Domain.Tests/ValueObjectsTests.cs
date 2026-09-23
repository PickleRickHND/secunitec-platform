namespace Secunitec.Billing.Domain.Tests;

public sealed class ValueObjectsTests
{
    [Theory]
    [InlineData("08011999123456")]
    [InlineData("00000000000000")]
    public void Rtn_De14Digitos_Aceptado(string valor) => Assert.Equal(valor, new Rtn(valor).Value);

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("0801199912345")]
    [InlineData("080119991234567")]
    [InlineData("0801-1999-12345")]
    [InlineData("08011999l23456")]
    public void Rtn_Invalido_Rechazado(string valor) =>
        Assert.Equal(BillingErrorCodes.RtnInvalido, Assert.Throws<BillingRuleException>(() => new Rtn(valor)).Code);

    [Theory]
    [InlineData("A1B2C3-D4E5F6-A7B8C9-D0E1F2-A3B4C5-D6")]
    [InlineData("000000-000000-000000-000000-000000-00")]
    public void Cai_ConFormatoDelSar_Aceptado(string valor) => Assert.Equal(valor, new Cai(valor).Value);

    [Theory]
    [InlineData("")]
    [InlineData("a1b2c3-d4e5f6-a7b8c9-d0e1f2-a3b4c5-d6")]
    [InlineData("A1B2C3-D4E5F6-A7B8C9-D0E1F2-A3B4C5")]
    [InlineData("A1B2C3D4E5F6A7B8C9D0E1F2A3B4C5D6")]
    [InlineData("A1B2C3-D4E5F6-A7B8C9-D0E1F2-A3B4C5-D6X")]
    public void Cai_Invalido_Rechazado(string valor) =>
        Assert.Equal(BillingErrorCodes.CaiInvalido, Assert.Throws<BillingRuleException>(() => new Cai(valor)).Code);

    [Fact]
    public void ValueObjects_SeComparanPorValor()
    {
        Assert.Equal(new Rtn("08011999123456"), new Rtn("08011999123456"));
        Assert.Equal(Money.Of(10), Money.Of(10.00m));
        Assert.True(Money.Of(1) < Money.Of(2));
    }
}
