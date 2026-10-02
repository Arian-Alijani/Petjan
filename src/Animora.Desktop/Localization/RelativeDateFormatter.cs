using System;

namespace Animora.Desktop.Localization;

/// <summary>
/// Renders the reference's relative Jalali date/age phrases (Animora-UI/assets/
/// patients-data.js <c>sinceJalali</c>/<c>untilJalali</c>/<c>ageFromJalali</c>)
/// using .NET date math and the Phase 04 Persian digit helper — never the
/// reference's JS. Also exposes the raw day deltas so callers can apply the
/// reference's "overdue" styling (e.g. the next-vaccine danger colour).
/// </summary>
public static class RelativeDateFormatter
{
    // Reference constants (patients-data.js): tropical-year and average-month
    // day counts used to derive an age in years/months.
    private const double DaysPerYear = 365.2425;
    private const double DaysPerMonth = 30.4375;

    /// <summary>
    /// "امروز" / "دیروز" / "N روز پیش" / "N هفته پیش" / "N ماه پیش" /
    /// "N سال پیش" from a past date (reference <c>sinceJalali</c>).
    /// </summary>
    public static string Since(DateTime date)
    {
        var days = DaysBetween(date);
        if (days <= 0)
        {
            return "امروز";
        }

        if (days == 1)
        {
            return "دیروز";
        }

        if (days < 7)
        {
            return $"{Fa(days)} روز پیش";
        }

        if (days < 31)
        {
            return $"{Fa(days / 7)} هفته پیش";
        }

        if (days < 365)
        {
            return $"{Fa(days / 30)} ماه پیش";
        }

        return $"{Fa(days / 365)} سال پیش";
    }

    /// <summary>
    /// "امروز" / "N روز دیگر" / "N ماه دیگر" / "N روز گذشته" from a future or
    /// past date, plus the raw signed day delta (reference <c>untilJalali</c>).
    /// </summary>
    public static (int Days, string Label) Until(DateTime date)
    {
        var days = (int)Math.Round((date.Date - DateTime.Today).TotalDays);
        string label;
        if (days == 0)
        {
            label = "امروز";
        }
        else if (days > 0)
        {
            label = days < 31 ? $"{Fa(days)} روز دیگر" : $"{Fa(days / 30)} ماه دیگر";
        }
        else
        {
            label = $"{Fa(-days)} روز گذشته";
        }

        return (days, label);
    }

    /// <summary>
    /// Human age from a birth date: a long label ("۳ سال و ۲ ماه") and a short
    /// card label ("۳ ساله" / "۵ ماهه") — reference <c>ageFromJalali</c>.
    /// </summary>
    public static (string Label, string Short) Age(DateTime birth)
    {
        var totalDays = Math.Max(0, DaysBetween(birth));
        var years = (int)(totalDays / DaysPerYear);
        var months = (int)((totalDays - (years * DaysPerYear)) / DaysPerMonth);

        string label;
        if (years >= 1)
        {
            label = months > 0 ? $"{Fa(years)} سال و {Fa(months)} ماه" : $"{Fa(years)} سال";
        }
        else if (months >= 1)
        {
            label = $"{Fa(months)} ماه";
        }
        else
        {
            label = $"{Fa(totalDays)} روز";
        }

        var shortLabel = years >= 1 ? $"{Fa(years)} ساله" : $"{Fa(months)} ماهه";
        return (label, shortLabel);
    }

    /// <summary>Age for an optional birth date; an unknown birth date renders an em-dash (never a bogus
    /// «2025 years» computed from <c>default(DateTime)</c>).</summary>
    public static (string Label, string Short) Age(DateTime? birth) =>
        birth is { } b ? Age(b) : ("—", "—");

    private static int DaysBetween(DateTime date) =>
        (int)Math.Round((DateTime.Today - date.Date).TotalDays);

    private static string Fa(int value) =>
        PersianNumberFormatter.ToPersianDigits(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
