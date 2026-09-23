using System.Text.RegularExpressions;

namespace Secunitec.Billing.Domain;

/// <summary>
/// Obligado tributario (tenant): su <see cref="Id"/> es el <c>tenant_id</c> del token. Tiene un CAI vigente con un
/// rango autorizado y una fecha límite de emisión; no emite fuera de ellos.
/// </summary>
public sealed partial class ObligadoTributario
{
    public const int MaxRazonSocial = 250;
    public const int MaxCorrelativo = 99_999_999;

    private ObligadoTributario() { }

    public ObligadoTributario(Guid id, Rtn rtn, string razonSocial, Cai cai, string prefijo,
        int rangoDesde, int rangoHasta, DateOnly fechaLimiteEmision)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(razonSocial) || razonSocial.Trim().Length > MaxRazonSocial)
        {
            throw new BillingRuleException(BillingErrorCodes.ObligadoInvalido, "La razón social es obligatoria (hasta 250 caracteres).");
        }

        Id = id;
        Rtn = rtn ?? throw new ArgumentNullException(nameof(rtn));
        RazonSocial = razonSocial.Trim();
        Activo = true;
        AsignarCai(cai, prefijo, rangoDesde, rangoHasta, fechaLimiteEmision);
    }

    public Guid Id { get; private set; }
    public Rtn Rtn { get; private set; } = null!;
    public string RazonSocial { get; private set; } = "";
    public Cai Cai { get; private set; } = null!;
    public string Prefijo { get; private set; } = "";
    public int RangoDesde { get; private set; }
    public int RangoHasta { get; private set; }
    public int SiguienteCorrelativo { get; private set; }
    public DateOnly FechaLimiteEmision { get; private set; }
    public bool Activo { get; private set; }

    /// <summary>
    /// Registra un CAI nuevo del SAR: rango y fecha límite nuevos y el correlativo vuelve al inicio del rango. Si el
    /// rango nuevo repite números ya emitidos con el mismo prefijo, el índice único de la base lo rechaza (409).
    /// </summary>
    public void ActualizarCai(Cai cai, string prefijo, int rangoDesde, int rangoHasta, DateOnly fechaLimiteEmision) =>
        AsignarCai(cai, prefijo, rangoDesde, rangoHasta, fechaLimiteEmision);

    public void Activar() => Activo = true;

    public void Desactivar() => Activo = false;

    // R07: el repositorio bloquea esta fila en la misma transacción que guarda la factura.
    public string ReservarNumero(DateOnly hoy)
    {
        if (!Activo)
        {
            throw new BillingRuleException(BillingErrorCodes.ObligadoInactivo, "El obligado está desactivado y no puede emitir.");
        }

        if (hoy > FechaLimiteEmision)
        {
            throw new BillingRuleException(BillingErrorCodes.CaiVencido, "El CAI venció: registre uno nuevo para seguir emitiendo.");
        }

        if (SiguienteCorrelativo > RangoHasta)
        {
            throw new BillingRuleException(BillingErrorCodes.RangoAgotado, "Se agotó el rango autorizado del CAI.");
        }

        return $"{Prefijo}-{SiguienteCorrelativo++:D8}";
    }

    private void AsignarCai(Cai cai, string prefijo, int rangoDesde, int rangoHasta, DateOnly fechaLimiteEmision)
    {
        if (prefijo is null || !PrefijoPattern().IsMatch(prefijo) ||
            rangoDesde < 1 || rangoHasta < rangoDesde || rangoHasta > MaxCorrelativo)
        {
            throw new BillingRuleException(BillingErrorCodes.RangoInvalido,
                "El prefijo debe tener el formato 000-001-01 y el rango autorizado debe ser creciente y positivo.");
        }

        Cai = cai ?? throw new ArgumentNullException(nameof(cai));
        Prefijo = prefijo;
        RangoDesde = rangoDesde;
        RangoHasta = rangoHasta;
        SiguienteCorrelativo = rangoDesde;
        FechaLimiteEmision = fechaLimiteEmision;
    }

    [GeneratedRegex("^[0-9]{3}-[0-9]{3}-[0-9]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex PrefijoPattern();
}
