using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Animora.Persistence.Sqlite.Converters;

/// <summary>
/// Stores <c>Quantity.Value</c> as an exact INTEGER at fixed scale ×1000 (3 dp) so SQLite <c>SUM</c> is exact.
/// Values with more than 3 decimals are rejected instead of silently rounded.
/// </summary>
public sealed class FixedScaleQuantityConverter() : ValueConverter<decimal, long>(
    value => ToScaled(value),
    scaled => scaled / Factor)
{
    public const decimal Factor = 1000m;

    public static long ToScaled(decimal value)
    {
        var scaled = value * Factor;
        if (scaled != decimal.Truncate(scaled))
            throw new InvalidOperationException($"Quantity {value} exceeds the stored precision of 3 decimal places.");
        return checked((long)scaled);
    }
}
