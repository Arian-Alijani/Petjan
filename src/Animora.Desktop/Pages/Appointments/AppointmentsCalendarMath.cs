using System;
using Animora.Desktop.Localization;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// Jalali/date helpers for the appointments calendar, transcribed from the
/// reference's date utility block in Animora-UI/assets/appointments.js
/// (<c>wIdx</c>/<c>weekStart</c>/<c>jMonthStart</c>/<c>jFull</c>/<c>jDate</c>).
/// All Jalali conversion delegates to Phase 04's <see cref="JalaliDate"/> — no
/// Jalali math is re-derived here (per the phase file).
/// </summary>
public static class AppointmentsCalendarMath
{
    /// <summary>Jalali weekday names, Saturday-first (reference's JDOW).</summary>
    public static readonly string[] DayNames =
    [
        "شنبه", "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه",
    ];

    /// <summary>Short Jalali weekday letters, Saturday-first (reference's JDOW_S).</summary>
    public static readonly string[] DayShortNames = ["ش", "ی", "د", "س", "چ", "پ", "ج"];

    /// <summary>Jalali weekday index: Saturday = 0 .. Friday = 6 (reference's <c>wIdx</c>).</summary>
    public static int WeekIndex(DateOnly d) => ((int)d.DayOfWeek + 1) % 7;

    /// <summary>Is this date a Friday (the clinic's closed day)?</summary>
    public static bool IsFriday(DateOnly d) => WeekIndex(d) == 6;

    /// <summary>The Saturday that starts the Jalali week containing <paramref name="d"/>.</summary>
    public static DateOnly WeekStart(DateOnly d) => d.AddDays(-WeekIndex(d));

    /// <summary>The first day of the Jalali month containing <paramref name="d"/>.</summary>
    public static DateOnly MonthStart(DateOnly d)
    {
        var x = d;
        while (JalaliDate.FromDateTime(x.ToDateTime(TimeOnly.MinValue)).Day != 1)
        {
            x = x.AddDays(-1);
        }

        return x;
    }

    public static int Year(DateOnly d) => JalaliDate.FromDateTime(d.ToDateTime(TimeOnly.MinValue)).Year;

    public static int Month(DateOnly d) => JalaliDate.FromDateTime(d.ToDateTime(TimeOnly.MinValue)).Month;

    public static int Day(DateOnly d) => JalaliDate.FromDateTime(d.ToDateTime(TimeOnly.MinValue)).Day;

    /// <summary>"۱۴ مهر ۱۴۰۴" — day, month name, year (reference's <c>jFull</c>).</summary>
    public static string Full(DateOnly d) => JalaliDate.FromDateTime(d.ToDateTime(TimeOnly.MinValue)).ToString("long");

    /// <summary>"۱۴ مهر" — day + month name (reference's <c>jDate</c>).</summary>
    public static string Medium(DateOnly d) => JalaliDate.FromDateTime(d.ToDateTime(TimeOnly.MinValue)).ToString("medium");

    /// <summary>Persian-digit day number (reference's <c>fa(p.d)</c>).</summary>
    public static string DayNumber(DateOnly d) => PersianNumberFormatter.ToPersianDigits(Day(d));

    /// <summary>The Jalali month name for a date (reference's <c>JMONTH[p.m - 1]</c>).</summary>
    public static string MonthName(DateOnly d) => PersianMonthNames.Get(Month(d));

    /// <summary>Persian-digit year (reference's <c>fa(p.y)</c>).</summary>
    public static string YearText(DateOnly d) => PersianNumberFormatter.ToPersianDigits(Year(d));
}
