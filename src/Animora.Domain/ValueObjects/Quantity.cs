namespace Animora.Domain.ValueObjects;

/// <summary>
/// A quantity of goods or services: a <see cref="decimal"/> magnitude plus a unit code
/// (e.g. <c>"EA"</c>, <c>"ML"</c>, <c>"MG"</c>). <c>decimal</c> is correct here — unlike money,
/// quantities are genuinely fractional (dose volumes, weights).
/// </summary>
/// <param name="Value">
/// The magnitude. Negative values are legal: stock <i>movements</i> are signed. A
/// non-negative invariant belongs to the on-hand aggregate (A07), not to this type.
/// </param>
/// <param name="UnitCode">Unit of measure code; equality is ordinal.</param>
public readonly record struct Quantity(decimal Value, string UnitCode)
{
    /// <summary>Zero in the given unit.</summary>
    public static Quantity Zero(string unitCode) => new(0m, unitCode);

    /// <summary><see langword="true"/> when the magnitude is exactly zero.</summary>
    public bool IsZero => Value == 0m;

    /// <summary><see langword="true"/> when the magnitude is below zero.</summary>
    public bool IsNegative => Value < 0m;

    /// <summary>Absolute magnitude, same unit.</summary>
    public Quantity Abs() => this with { Value = Math.Abs(Value) };

    /// <summary>Named alternative to <c>operator +</c>.</summary>
    public Quantity Add(Quantity other)
    {
        EnsureSameUnit(this, other);
        return new Quantity(Value + other.Value, UnitCode);
    }

    /// <summary>Named alternative to <c>operator -</c>.</summary>
    public Quantity Subtract(Quantity other)
    {
        EnsureSameUnit(this, other);
        return new Quantity(Value - other.Value, UnitCode);
    }

    /// <summary>Named alternative to unary <c>operator -</c>.</summary>
    public Quantity Negate() => new(-Value, UnitCode);

    /// <inheritdoc cref="Add(Quantity)" />
    public static Quantity operator +(Quantity left, Quantity right) => left.Add(right);

    /// <inheritdoc cref="Subtract(Quantity)" />
    public static Quantity operator -(Quantity left, Quantity right) => left.Subtract(right);

    /// <inheritdoc cref="Negate()" />
    public static Quantity operator -(Quantity value) => value.Negate();

    private static void EnsureSameUnit(Quantity left, Quantity right)
    {
        if (!string.Equals(left.UnitCode, right.UnitCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unit mismatch: '{left.UnitCode}' and '{right.UnitCode}' cannot be combined.");
        }
    }
}
