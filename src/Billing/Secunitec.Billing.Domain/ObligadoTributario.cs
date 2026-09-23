namespace Secunitec.Billing.Domain;

public sealed class ObligadoTributario
{
    private ObligadoTributario() { }

    public ObligadoTributario(Guid id, string rtn, string razonSocial, string cai, string prefijo,
        int rangoDesde, int rangoHasta, DateOnly fechaLimiteEmision)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(razonSocial) || razonSocial.Trim().Length > 250 ||
            rangoDesde < 1 || rangoHasta < rangoDesde || rangoHasta > 99_999_999 ||
            prefijo is null || !System.Text.RegularExpressions.Regex.IsMatch(prefijo, "^[0-9]{3}-[0-9]{3}-[0-9]{2}$"))
        {
            throw new BillingRuleException("Los datos del obligado o el rango autorizado son inválidos.");
        }

        Id = id;
        Rtn = TaxIdentity.Rtn(rtn);
        RazonSocial = razonSocial.Trim();
        Cai = TaxIdentity.Cai(cai);
        Prefijo = prefijo;
        RangoDesde = rangoDesde;
        RangoHasta = rangoHasta;
        SiguienteCorrelativo = rangoDesde;
        FechaLimiteEmision = fechaLimiteEmision;
        Activo = true;
    }

    public Guid Id { get; private set; }
    public string Rtn { get; private set; } = "";
    public string RazonSocial { get; private set; } = "";
    public string Cai { get; private set; } = "";
    public string Prefijo { get; private set; } = "";
    public int RangoDesde { get; private set; }
    public int RangoHasta { get; private set; }
    public int SiguienteCorrelativo { get; private set; }
    public DateOnly FechaLimiteEmision { get; private set; }
    public bool Activo { get; private set; }

    // R07: el repositorio bloquea esta fila en la misma transacción que guarda la factura.
    public string ReservarNumero(DateOnly hoy)
    {
        if (!Activo || hoy > FechaLimiteEmision || SiguienteCorrelativo > RangoHasta)
        {
            throw new BillingRuleException("El CAI no está vigente o el rango autorizado se agotó.");
        }

        return $"{Prefijo}-{SiguienteCorrelativo++:D8}";
    }
}
