namespace Secunitec.Billing.Domain.Tests;

// Regresión: textos de más de 250 caracteres llegaban a Postgres y la API respondía 500 (error 22001).
public sealed class LimitesDeTextoTests
{
    private static string Largo => new('a', 251);

    [Fact]
    public void Cliente_NombreDe251Caracteres_Rechazado() =>
        Assert.Throws<BillingRuleException>(() => new Cliente(Guid.NewGuid(), Guid.NewGuid(), Largo, null, null));

    [Fact]
    public void Cliente_EmailDe251Caracteres_Rechazado() =>
        Assert.Throws<BillingRuleException>(() => new Cliente(Guid.NewGuid(), Guid.NewGuid(), "Cliente", null, Largo));

    [Fact]
    public void Cliente_NombreDe250ConEspacios_Aceptado()
    {
        Cliente cliente = new(Guid.NewGuid(), Guid.NewGuid(), $"  {new string('a', 250)}  ", null, null);
        Assert.Equal(250, cliente.Nombre.Length);
    }

    [Fact]
    public void Obligado_RazonSocialDe251Caracteres_Rechazada() =>
        Assert.Throws<BillingRuleException>(() => new ObligadoTributario(Guid.NewGuid(), "08011999123456", Largo,
            "AAAAAA-BBBBBB-CCCCCC-DDDDDD-EEEEEE-FF", "000-001-01", 1, 10, new DateOnly(2026, 12, 31)));
}
