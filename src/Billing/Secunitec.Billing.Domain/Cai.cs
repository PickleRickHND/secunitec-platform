using System.Text.RegularExpressions;

namespace Secunitec.Billing.Domain;

/// <summary>
/// Código de Autorización de Impresión del SAR: seis grupos alfanuméricos en mayúscula separados por guion
/// (6-6-6-6-6-2, 37 caracteres).
/// </summary>
public sealed partial record Cai
{
    public Cai(string value)
    {
        if (value is null || !Pattern().IsMatch(value))
        {
            throw new BillingRuleException(BillingErrorCodes.CaiInvalido, "El CAI tiene un formato inválido.");
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;

    [GeneratedRegex("^[0-9A-Z]{6}-[0-9A-Z]{6}-[0-9A-Z]{6}-[0-9A-Z]{6}-[0-9A-Z]{6}-[0-9A-Z]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
