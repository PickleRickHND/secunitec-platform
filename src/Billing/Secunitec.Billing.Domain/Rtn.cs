using System.Text.RegularExpressions;

namespace Secunitec.Billing.Domain;

/// <summary>Registro Tributario Nacional de Honduras: 14 dígitos.</summary>
public sealed partial record Rtn
{
    public Rtn(string value)
    {
        if (value is null || !Pattern().IsMatch(value))
        {
            throw new BillingRuleException(BillingErrorCodes.RtnInvalido, "El RTN debe tener 14 dígitos.");
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;

    [GeneratedRegex("^[0-9]{14}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
