using System.Text.RegularExpressions;

namespace Secunitec.Billing.Domain;

public static partial class TaxIdentity
{
    [GeneratedRegex("^[0-9]{14}$", RegexOptions.CultureInvariant)]
    private static partial Regex RtnPattern();

    [GeneratedRegex("^[0-9A-Z]{6}-[0-9A-Z]{6}-[0-9A-Z]{6}-[0-9A-Z]{6}-[0-9A-Z]{6}-[0-9A-Z]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex CaiPattern();

    public static string Rtn(string value)
    {
        if (value is null || !RtnPattern().IsMatch(value))
        {
            throw new BillingRuleException("El RTN debe tener 14 dígitos.");
        }

        return value;
    }

    public static string Cai(string value)
    {
        if (value is null || !CaiPattern().IsMatch(value))
        {
            throw new BillingRuleException("El CAI tiene un formato inválido.");
        }

        return value;
    }
}
