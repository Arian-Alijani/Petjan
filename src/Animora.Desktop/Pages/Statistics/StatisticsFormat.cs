using System;
using System.Collections.Generic;
using System.Globalization;
using Animora.Contracts.Statistics;
using Animora.Desktop.Localization;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Statistics;

/// <summary>
/// Presentation helpers of the «آمار» page: money / percent / Jalali formatting plus the static Persian lookup tables for
/// expense categories and revenue sources. Display names live here (never in XAML or the query layer); an unknown code
/// falls back to the raw code so a new backend value is visible rather than blank.
/// </summary>
public static class StatisticsFormat
{
    public const string Currency = "تومان";

    private static readonly Dictionary<string, string> CategoryNames = new(StringComparer.Ordinal)
    {
        ["RENT"] = "اجاره",
        ["UTILITIES"] = "قبوض و انرژی",
        ["SALARY"] = "حقوق و دستمزد",
        ["SUPPLIES"] = "ملزومات و تجهیزات",
        ["MAINTENANCE"] = "تعمیر و نگهداری",
        ["MARKETING"] = "تبلیغات و بازاریابی",
        ["TAX"] = "مالیات و عوارض",
        ["TRANSPORT"] = "حمل و نقل",
        ["OTHER"] = "سایر",
    };

    private static readonly Dictionary<string, string> CategoryColors = new(StringComparer.Ordinal)
    {
        ["RENT"] = "#0d9488",
        ["UTILITIES"] = "#1570cd",
        ["SALARY"] = "#7c3aed",
        ["SUPPLIES"] = "#f79009",
        ["MAINTENANCE"] = "#d92d20",
        ["MARKETING"] = "#ee46bc",
        ["TAX"] = "#475467",
        ["TRANSPORT"] = "#06aed4",
        ["OTHER"] = "#98a2b3",
    };

    private static readonly Dictionary<string, string> SourceNames = new(StringComparer.Ordinal)
    {
        ["visit"] = "ویزیت‌ها",
        ["product"] = "فروش کالا",
        ["boarding"] = "پانسیون",
        ["other"] = "سایر",
    };

    private static readonly Dictionary<string, string> SourceColors = new(StringComparer.Ordinal)
    {
        ["visit"] = "#0d9488",
        ["product"] = "#1570cd",
        ["boarding"] = "#7c3aed",
        ["other"] = "#98a2b3",
    };

    public static string CategoryName(string code) => CategoryNames.GetValueOrDefault(code, code);

    public static string CategoryColor(string code) => CategoryColors.GetValueOrDefault(code, "#98a2b3");

    public static IReadOnlyList<string> CategoryCodes { get; } = new[]
    {
        "RENT", "UTILITIES", "SALARY", "SUPPLIES", "MAINTENANCE", "MARKETING", "TAX", "TRANSPORT", "OTHER",
    };

    public static string SourceName(string source) => SourceNames.GetValueOrDefault(source, source);

    public static string SourceColor(string source) => SourceColors.GetValueOrDefault(source, "#98a2b3");

    public static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    /// <summary>«۱٬۲۳۴٬۵۶۷ تومان».</summary>
    public static string Money(long minor) => $"{PersianNumberFormatter.FormatGrouped(minor)} {Currency}";

    /// <summary>Grouped Persian number without the currency suffix.</summary>
    public static string Number(long value) => PersianNumberFormatter.FormatGrouped(value);

    public static string Count(int value) => PersianNumberFormatter.ToPersianDigits(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Persian quantity: whole numbers grouped, fractions to at most two decimals.</summary>
    public static string Quantity(decimal value) => PersianNumberFormatter.ToPersianDigits(
        value == Math.Truncate(value)
            ? ((long)value).ToString("N0", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture));

    /// <summary>«۲۵٪» (one share of a whole); empty when the whole is zero.</summary>
    public static string Percent(long part, long whole) =>
        whole <= 0 ? string.Empty : PersianNumberFormatter.ToPersianDigits(
            Math.Round(part * 100d / whole, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)) + "٪";

    /// <summary>
    /// Change versus the previous comparable window as a signed percent. <paramref name="higherIsBetter"/> flips the
    /// colour for costs. No previous value ⇒ «جدید» (or «—» when both are zero).
    /// </summary>
    public static (string Text, bool IsPositive) Delta(long current, long previous, bool higherIsBetter = true)
    {
        if (previous == 0) return current == 0 ? ("—", true) : ("جدید", higherIsBetter);
        var change = (current - previous) * 100d / Math.Abs(previous);
        var rounded = Math.Round(change, MidpointRounding.AwayFromZero);
        var digits = PersianNumberFormatter.ToPersianDigits(Math.Abs(rounded).ToString("0", CultureInfo.InvariantCulture));
        var text = rounded == 0 ? "۰٪" : rounded > 0 ? $"▲ {digits}٪" : $"▼ {digits}٪";
        var up = change >= 0;
        return (text, higherIsBetter ? up : !up);
    }

    /// <summary>Jalali «۱۴ مهر» for a Gregorian day.</summary>
    public static string JalaliMedium(DateOnly day) => JalaliDate.FromDateTime(day.ToDateTime(TimeOnly.MinValue)).ToString("medium");

    /// <summary>Jalali «۱۴۰۵/۰۷/۱۴» for a Gregorian day.</summary>
    public static string JalaliShort(DateOnly day) => JalaliDate.FromDateTime(day.ToDateTime(TimeOnly.MinValue)).ToString("short");

    public static string JalaliShort(DateTimeOffset moment) => JalaliShort(DateOnly.FromDateTime(moment.LocalDateTime));

    /// <summary>Jalali month name of a Gregorian day (month-bucket axis label).</summary>
    public static string JalaliMonthName(DateOnly day) => JalaliDate.FromDateTime(day.ToDateTime(TimeOnly.MinValue)).MonthName;

    /// <summary>Axis label of a chart bucket: the month name when buckets are ≥ a month apart, else «۱۴ مهر».</summary>
    public static string BucketLabel(DateOnly start, bool monthly) => monthly ? JalaliMonthName(start) : JalaliMedium(start);

    /// <summary>True when consecutive bucket starts are about a month apart (the <c>ytd</c> month bucket).</summary>
    public static bool IsMonthly(IReadOnlyList<StatisticsSeriesPoint> series) =>
        series.Count >= 2 && series[1].Start.DayNumber - series[0].Start.DayNumber >= 28;

    /// <summary>Converts a Jalali ASCII <c>yyyy/MM/dd</c> picker value to the Gregorian <c>yyyyMMdd</c> window key.</summary>
    public static int? ToGregorianKey(string? jalali)
    {
        if (string.IsNullOrWhiteSpace(jalali)) return null;
        try
        {
            var date = JalaliCalendar.Parse(PersianNumberFormatter.ToAsciiDigits(jalali)).ToDateTime();
            return date.Year * 10000 + date.Month * 100 + date.Day;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or IndexOutOfRangeException or OverflowException)
        {
            return null;
        }
    }
}
