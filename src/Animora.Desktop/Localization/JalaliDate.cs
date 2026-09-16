using System.Globalization;

namespace Animora.Desktop.Localization;

/// <summary>
/// A lightweight Persian (Jalali) calendar date. Conversion is delegated to
/// .NET's built-in <see cref="PersianCalendar"/> (a correct, shipped
/// implementation) — this struct only layers the Persian month-name table and
/// Persian-digit formatting on top, matching the reference's <c>J.long</c>/
/// <c>J.medium</c> display shapes (Animora-UI/assets/cheques-data.js).
/// </summary>
public readonly struct JalaliDate : System.IEquatable<JalaliDate>
{
    private static readonly PersianCalendar Calendar = new();

    public JalaliDate(int year, int month, int day)
    {
        if (month is < 1 or > 12)
        {
            throw new System.ArgumentOutOfRangeException(nameof(month), month, "Jalali month must be in 1..12.");
        }

        if (day < 1 || day > Calendar.GetDaysInMonth(year, month))
        {
            throw new System.ArgumentOutOfRangeException(nameof(day), day, "Day is out of range for the given Jalali year/month.");
        }

        Year = year;
        Month = month;
        Day = day;
    }

    /// <summary>Jalali year (e.g. 1404).</summary>
    public int Year { get; }

    /// <summary>Jalali month, 1-based (1 = Farvardin .. 12 = Esfand).</summary>
    public int Month { get; }

    /// <summary>Jalali day of month, 1-based.</summary>
    public int Day { get; }

    /// <summary>Today's date in the Jalali calendar (local time).</summary>
    public static JalaliDate Today => FromDateTime(System.DateTime.Now);

    /// <summary>Converts a Gregorian <see cref="System.DateTime"/> to its Jalali equivalent.</summary>
    public static JalaliDate FromDateTime(System.DateTime value)
    {
        return new JalaliDate(
            Calendar.GetYear(value),
            Calendar.GetMonth(value),
            Calendar.GetDayOfMonth(value));
    }

    /// <summary>Converts this Jalali date back to a Gregorian <see cref="System.DateTime"/> (midnight).</summary>
    public System.DateTime ToDateTime()
    {
        return Calendar.ToDateTime(Year, Month, Day, 0, 0, 0, 0);
    }

    /// <summary>
    /// The Persian month name for this date (فروردین..اسفند).
    /// </summary>
    public string MonthName => PersianMonthNames.Get(Month);

    /// <summary>
    /// Formats this date. Supported formats:
    /// <list type="bullet">
    /// <item><description><c>"long"</c> (default): "۱۴ مهر ۱۴۰۴" — day, month name, year.</description></item>
    /// <item><description><c>"medium"</c>: "۱۴ مهر" — day, month name.</description></item>
    /// <item><description><c>"short"</c>: "۱۴۰۴/۰۷/۱۴" — zero-padded numeric, Persian digits.</description></item>
    /// </list>
    /// </summary>
    public string ToString(string? format)
    {
        var day = PersianNumberFormatter.ToPersianDigits(Day.ToString(CultureInfo.InvariantCulture));

        switch (format?.ToLowerInvariant())
        {
            case "medium":
                return $"{day} {MonthName}";

            case "short":
            {
                var y = Year.ToString("D4", CultureInfo.InvariantCulture);
                var m = Month.ToString("D2", CultureInfo.InvariantCulture);
                var d = Day.ToString("D2", CultureInfo.InvariantCulture);
                return PersianNumberFormatter.ToPersianDigits($"{y}/{m}/{d}");
            }

            case null:
            case "":
            case "long":
            {
                var year = PersianNumberFormatter.ToPersianDigits(Year.ToString(CultureInfo.InvariantCulture));
                return $"{day} {MonthName} {year}";
            }

            default:
                throw new System.FormatException($"Unsupported JalaliDate format: '{format}'.");
        }
    }

    /// <summary>Formats this date in the default "long" shape (e.g. "۱۴ مهر ۱۴۰۴").</summary>
    public override string ToString() => ToString("long");

    public bool Equals(JalaliDate other) => Year == other.Year && Month == other.Month && Day == other.Day;

    public override bool Equals(object? obj) => obj is JalaliDate other && Equals(other);

    public override int GetHashCode() => System.HashCode.Combine(Year, Month, Day);

    public static bool operator ==(JalaliDate left, JalaliDate right) => left.Equals(right);

    public static bool operator !=(JalaliDate left, JalaliDate right) => !left.Equals(right);
}
