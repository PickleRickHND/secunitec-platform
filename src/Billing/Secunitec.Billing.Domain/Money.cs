namespace Secunitec.Billing.Domain;

/// <summary>
/// Monto en lempiras con dos decimales, redondeado "half away from zero" (el redondeo comercial que usa el SAR).
/// Nunca es negativo y cabe en <c>NUMERIC(18,2)</c>.
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    /// <summary>Máximo que admite <c>NUMERIC(18,2)</c>.</summary>
    public const decimal Maximo = 9_999_999_999_999_999.99m;

    private Money(decimal amount)
    {
        Amount = amount;
    }

    public static Money Zero { get; } = new(0m);

    public decimal Amount { get; }

    /// <summary>Redondea a dos decimales y valida el rango.</summary>
    public static Money Of(decimal amount)
    {
        decimal rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (rounded is < 0 or > Maximo)
        {
            throw new BillingRuleException(BillingErrorCodes.MontoInvalido, "El monto está fuera del rango permitido.");
        }

        return new Money(rounded);
    }

    public static Money operator +(Money left, Money right) => Of(left.Amount + right.Amount);

    public Money Por(decimal factor) => Of(Amount * factor);

    public int CompareTo(Money other) => Amount.CompareTo(other.Amount);

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    public override string ToString() => Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}
