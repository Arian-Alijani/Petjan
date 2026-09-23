namespace Animora.Domain.ValueObjects;

/// <summary>
/// A monetary amount held as an integer count of <b>minor units</b> plus a 3-letter
/// ISO-4217 currency code (STACK.md §3.3, R-B24). No <c>decimal</c> money field exists
/// anywhere in the product: all arithmetic is exact integer arithmetic, and the only
/// rounding boundary is <see cref="ApplyPercent(decimal)"/>.
/// </summary>
/// <param name="Minor">Amount in minor units (for <c>IRT</c>, scale 0, this is whole toman).</param>
/// <param name="Currency">ISO-4217 alphabetic code, uppercase, 3 letters. Default product currency: <c>IRT</c>.</param>
/// <remarks>
/// A currency mismatch in any binary operation is a <b>defect</b>, not expected control flow,
/// and therefore throws <see cref="InvalidOperationException"/> rather than returning a
/// <c>Result</c> (CONVENTIONS.md §4).
/// </remarks>
public readonly record struct Money(long Minor, string Currency)
    : IComparable<Money>, IComparable
{
    /// <summary>Zero toman — the product's neutral element.</summary>
    public static readonly Money ZeroIrt = new(0, CurrencyScale.Irt);

    /// <summary>Creates an amount in the product default currency (<c>IRT</c>, scale 0).</summary>
    public static Money Irt(long toman) => new(toman, CurrencyScale.Irt);

    /// <summary>Creates an amount after validating the currency code shape.</summary>
    /// <exception cref="ArgumentException">The code is not 3 uppercase ASCII letters.</exception>
    public static Money Of(long minor, string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        if (currency.Length != 3 || !currency.All(static c => c is >= 'A' and <= 'Z'))
        {
            throw new ArgumentException($"Currency must be a 3-letter uppercase ISO code; got '{currency}'.", nameof(currency));
        }

        return new Money(minor, currency);
    }

    /// <summary>Zero in the given currency.</summary>
    public static Money Zero(string currency) => new(0, currency);

    /// <summary>Minor-unit digit count of this amount's currency.</summary>
    public int Scale => CurrencyScale.Of(Currency);

    /// <summary><see langword="true"/> when the amount is exactly zero.</summary>
    public bool IsZero => Minor == 0;

    /// <summary><see langword="true"/> when the amount is below zero.</summary>
    public bool IsNegative => Minor < 0;

    /// <summary>Absolute value, same currency.</summary>
    public Money Abs() => this with { Minor = Math.Abs(Minor) };

    // ---- arithmetic ---------------------------------------------------------

    /// <summary>Named alternative to <c>operator +</c>.</summary>
    public Money Add(Money other)
    {
        EnsureSameCurrency(this, other);
        return new Money(checked(Minor + other.Minor), Currency);
    }

    /// <summary>Named alternative to <c>operator -</c>.</summary>
    public Money Subtract(Money other)
    {
        EnsureSameCurrency(this, other);
        return new Money(checked(Minor - other.Minor), Currency);
    }

    /// <summary>Named alternative to unary <c>operator -</c>.</summary>
    public Money Negate() => new(checked(-Minor), Currency);

    /// <summary>Named alternative to <c>operator *</c>.</summary>
    public Money Multiply(long factor) => new(checked(Minor * factor), Currency);

    /// <inheritdoc cref="Add(Money)" />
    public static Money operator +(Money left, Money right) => left.Add(right);

    /// <inheritdoc cref="Subtract(Money)" />
    public static Money operator -(Money left, Money right) => left.Subtract(right);

    /// <inheritdoc cref="Negate()" />
    public static Money operator -(Money value) => value.Negate();

    /// <inheritdoc cref="Multiply(long)" />
    public static Money operator *(Money value, int factor) => value.Multiply(factor);

    /// <inheritdoc cref="Multiply(long)" />
    public static Money operator *(Money value, long factor) => value.Multiply(factor);

    /// <inheritdoc cref="Multiply(long)" />
    public static Money operator *(int factor, Money value) => value.Multiply(factor);

    /// <inheritdoc cref="Multiply(long)" />
    public static Money operator *(long factor, Money value) => value.Multiply(factor);

    // ---- comparison ---------------------------------------------------------

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Currencies differ.</exception>
    public int CompareTo(Money other)
    {
        EnsureSameCurrency(this, other);
        return Minor.CompareTo(other.Minor);
    }

    /// <inheritdoc />
    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        Money other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(Money)}.", nameof(obj)),
    };

    /// <summary>Ordinal comparison within the same currency.</summary>
    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    /// <inheritdoc cref="op_LessThan(Money, Money)" />
    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    /// <inheritdoc cref="op_LessThan(Money, Money)" />
    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    /// <inheritdoc cref="op_LessThan(Money, Money)" />
    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    // ---- money-specific math ------------------------------------------------

    /// <summary>
    /// Splits the amount into <paramref name="parts"/> shares using largest-remainder
    /// distribution: every share differs by at most one minor unit and the shares sum
    /// back to exactly this amount, so split/discount math never loses or invents a unit.
    /// Leftover minor units go to the first shares.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="parts"/> is not positive.</exception>
    public IReadOnlyList<Money> Allocate(int parts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(parts, 1);

        var quotient = Math.DivRem(Minor, parts, out var remainder);
        var step = Math.Sign(remainder);
        var left = Math.Abs(remainder);

        var result = new Money[parts];
        for (var i = 0; i < parts; i++)
        {
            var extra = i < left ? step : 0;
            result[i] = new Money(quotient + extra, Currency);
        }

        return result;
    }

    /// <summary>
    /// Returns <paramref name="percent"/>% of this amount, rounded on the minor unit with
    /// <see cref="MidpointRounding.AwayFromZero"/>. This is the single rounding policy of
    /// the whole product — no other code may round money.
    /// </summary>
    public Money ApplyPercent(decimal percent)
    {
        var raw = Minor * percent / 100m;
        var rounded = Math.Round(raw, 0, MidpointRounding.AwayFromZero);
        return new Money((long)rounded, Currency);
    }

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (!string.Equals(left.Currency, right.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Currency mismatch: '{left.Currency}' and '{right.Currency}' cannot be combined.");
        }
    }
}
