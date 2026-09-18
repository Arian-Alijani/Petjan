using System;
using System.Globalization;

namespace Animora.Desktop.Localization;

/// <summary>
/// Thin day-arithmetic helpers layered on the Phase 04 <see cref="JalaliDate"/>
/// and .NET's <see cref="PersianCalendar"/> — the desktop equivalent of the
/// reference's <c>window.J</c> date utilities (Animora-UI/assets/cheques-data.js:
/// <c>toDay</c>/<c>fromDay</c>/<c>parse</c>/<c>today</c>/<c>weekday</c>/<c>diff</c>/
/// <c>monthLen</c>). Written with .NET date idioms (absolute day numbers via
/// <see cref="DateOnly.DayNumber"/>) rather than porting the reference's JS
/// algorithm. Dates are transported as ASCII <c>yyyy/MM/dd</c> Jalali strings;
/// Persian-digit display goes through <see cref="JalaliDate.ToString(string)"/>.
/// </summary>
public static class JalaliCalendar
{
    private static readonly PersianCalendar Calendar = new();

    /// <summary>Parses an ASCII Jalali <c>yyyy/MM/dd</c> string into a <see cref="JalaliDate"/>.</summary>
    public static JalaliDate Parse(string value)
    {
        var parts = value.Split('/');
        return new JalaliDate(
            int.Parse(parts[0], CultureInfo.InvariantCulture),
            int.Parse(parts[1], CultureInfo.InvariantCulture),
            int.Parse(parts[2], CultureInfo.InvariantCulture));
    }

    /// <summary>Today in the Jalali calendar, as an ASCII <c>yyyy/MM/dd</c> string.</summary>
    public static string Today => ToIso(JalaliDate.Today);

    /// <summary>Formats a <see cref="JalaliDate"/> as an ASCII <c>yyyy/MM/dd</c> string.</summary>
    public static string ToIso(JalaliDate date) =>
        $"{date.Year:D4}/{date.Month:D2}/{date.Day:D2}";

    /// <summary>
    /// The absolute day number of a Jalali date (the reference's <c>toDay</c>),
    /// so two dates can be compared/subtracted.
    /// </summary>
    public static int ToDay(string value) =>
        DateOnly.FromDateTime(Parse(value).ToDateTime()).DayNumber;

    /// <summary>The Jalali date <paramref name="days"/> absolute days after the given day number.</summary>
    public static string FromDay(int dayNumber) =>
        ToIso(JalaliDate.FromDateTime(DateOnly.FromDayNumber(dayNumber).ToDateTime(TimeOnly.MinValue)));

    /// <summary>Adds <paramref name="days"/> to a Jalali date, returning an ASCII <c>yyyy/MM/dd</c> string.</summary>
    public static string AddDays(string value, int days) => FromDay(ToDay(value) + days);

    /// <summary>The day difference <c>to - from</c> (positive = future).</summary>
    public static int Diff(string to, string from) => ToDay(to) - ToDay(from);

    /// <summary>
    /// The day-of-week index in the reference's Saturday-first scheme
    /// (0 = شنبه .. 6 = جمعه).
    /// </summary>
    public static int Weekday(string value)
    {
        var dow = Parse(value).ToDateTime().DayOfWeek;
        return ((int)dow + 1) % 7;
    }

    /// <summary>The number of days in a Jalali month (mirrors <c>monthLen</c>).</summary>
    public static int MonthLen(int year, int month) => Calendar.GetDaysInMonth(year, month);

    /// <summary>Shifts a (year, month) by <paramref name="delta"/> months, rolling the year.</summary>
    public static (int Year, int Month) ShiftMonth(int year, int month, int delta)
    {
        month += delta;
        while (month > 12)
        {
            month -= 12;
            year++;
        }

        while (month < 1)
        {
            month += 12;
            year--;
        }

        return (year, month);
    }

    /// <summary>The Jalali <c>long</c> shape for an ASCII date («۱۷ مهر ۱۴۰۵»).</summary>
    public static string Long(string value) => Parse(value).ToString("long");

    /// <summary>The Jalali <c>medium</c> shape for an ASCII date («۱۷ مهر»).</summary>
    public static string Medium(string value) => Parse(value).ToString("medium");

    /// <summary>The Jalali <c>short</c> shape for an ASCII date («۱۴۰۵/۰۷/۱۷», Persian digits).</summary>
    public static string Short(string value) => Parse(value).ToString("short");
}
