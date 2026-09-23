namespace Animora.Domain.ValueObjects;

/// <summary>
/// Maps an ISO-4217 currency code to the number of decimal digits in its minor unit.
/// Money is stored as an integer count of minor units (STACK.md §3.3, R-B24); this
/// helper is the only place that knows how many of them make a major unit.
/// </summary>
public static class CurrencyScale
{
    /// <summary>The product's default currency: Iranian toman, a scale-0 currency.</summary>
    public const string Irt = "IRT";

    /// <summary>Scale used for any currency not explicitly known here.</summary>
    public const int DefaultScale = 2;

    /// <summary>
    /// Minor-unit digit count for <paramref name="currency"/>:
    /// <c>IRT</c> → 0, <c>USD</c>/<c>EUR</c> → 2, anything else → <see cref="DefaultScale"/>.
    /// </summary>
    public static int Of(string currency) => currency switch
    {
        Irt => 0,
        "USD" or "EUR" => 2,
        _ => DefaultScale,
    };
}
