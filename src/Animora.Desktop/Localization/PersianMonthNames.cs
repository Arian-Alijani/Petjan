namespace Animora.Desktop.Localization;

/// <summary>
/// The 12 Persian (Jalali) month names, index 0 = Farvardin .. index 11 = Esfand.
/// Mirrors the reference MONTHS table in Animora-UI/assets/cheques-data.js and
/// is consumed by <see cref="JalaliDate.ToString(string)"/>.
/// </summary>
public static class PersianMonthNames
{
    /// <summary>
    /// Month names indexed 0..11. Callers using a 1-based Jalali month must
    /// subtract 1 before indexing.
    /// </summary>
    public static readonly string[] Names =
    [
        "فروردین",
        "اردیبهشت",
        "خرداد",
        "تیر",
        "مرداد",
        "شهریور",
        "مهر",
        "آبان",
        "آذر",
        "دی",
        "بهمن",
        "اسفند",
    ];

    /// <summary>
    /// Returns the Persian name for a 1-based Jalali month (1 = Farvardin .. 12 = Esfand).
    /// </summary>
    /// <exception cref="System.ArgumentOutOfRangeException">If <paramref name="month"/> is not in 1..12.</exception>
    public static string Get(int month)
    {
        if (month is < 1 or > 12)
        {
            throw new System.ArgumentOutOfRangeException(nameof(month), month, "Jalali month must be in 1..12.");
        }

        return Names[month - 1];
    }
}
