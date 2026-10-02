using System;
using System.Collections.Generic;
using Animora.Contracts.Appointments;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// The visible hour window of the day/week time grid (A25), whole hours as minutes from midnight. It is derived per
/// render from the clinic's doctor schedules and the events actually on screen, so an appointment at 06:00 or 23:00
/// is never outside the grid. Every pixel measure the grid uses (hour rows, gutter, column height, event offsets, the
/// "now" line) comes from this one value instead of duplicated constants.
/// </summary>
public readonly record struct TimeGridWindow(int StartMinutes, int EndMinutes)
{
    /// <summary>The default 07:00–22:00 window used before anything widens it.</summary>
    public static TimeGridWindow Default { get; } = new(7 * 60, 22 * 60);

    /// <summary>Number of hour rows.</summary>
    public int HourCount => Math.Max(1, (EndMinutes - StartMinutes) / 60);

    /// <summary>Total grid body height in px.</summary>
    public double TotalHeight => AppointmentsLayout.PixelsPerHour * HourCount;

    /// <summary>Offset in px of <paramref name="minutes"/> from the first hour row, clamped inside the window.</summary>
    public double OffsetOf(int minutes) =>
        AppointmentsLayout.MinuteToPx(Math.Clamp(minutes, StartMinutes, EndMinutes) - StartMinutes);

    /// <summary>True when <paramref name="minutes"/> lies inside the visible window.</summary>
    public bool Contains(int minutes) => minutes >= StartMinutes && minutes <= EndMinutes;
}

/// <summary>
/// Time-grid layout constants. The pixel-per-hour constant lives in one place so event cards, the gutter hour rows
/// and the "now" line all agree on the same minute→pixel mapping.
/// </summary>
public static class AppointmentsLayout
{
    /// <summary>Pixels per hour row (reference's PXH).</summary>
    public const double PixelsPerHour = 78;

    /// <summary>Minimum event-card height in px (reference's 26px floor).</summary>
    public const double MinEventHeight = 26;

    /// <summary>Minimum readable width of one grid column; below this the grid scrolls horizontally.</summary>
    public const double MinColumnWidth = 132;

    /// <summary>Width of the hour gutter in px.</summary>
    public const double GutterWidth = 56;

    /// <summary>Converts a minute span to pixels at <see cref="PixelsPerHour"/>.</summary>
    public static double MinuteToPx(int minutes) => minutes / 60.0 * PixelsPerHour;

    /// <summary>
    /// The default window widened to cover every doctor's working window and every event in <paramref name="events"/>,
    /// rounded out to whole hours and clamped to the day.
    /// </summary>
    public static TimeGridWindow Fit(IEnumerable<AppointmentDoctor> doctors, IEnumerable<AppointmentEvent> events)
    {
        var start = TimeGridWindow.Default.StartMinutes;
        var end = TimeGridWindow.Default.EndMinutes;
        foreach (var d in doctors)
        {
            if (d.WorkEndMinute <= d.WorkStartMinute) continue;
            start = Math.Min(start, d.WorkStartMinute);
            end = Math.Max(end, d.WorkEndMinute);
        }

        foreach (var e in events)
        {
            start = Math.Min(start, e.StartMinutes);
            end = Math.Max(end, e.EndMinutes);
        }

        start = Math.Clamp(start / 60 * 60, 0, 23 * 60);
        end = Math.Clamp((end + 59) / 60 * 60, start + 60, 24 * 60);
        return new TimeGridWindow(start, end);
    }
}

/// <summary>One hour row of the grid: the gutter label and the matching separator line in every column.</summary>
public sealed class HourRowViewModel(string label, bool isFirst)
{
    public string Label { get; } = label;

    public bool IsFirst { get; } = isFirst;

    public double Height => AppointmentsLayout.PixelsPerHour;
}

/// <summary>Working-day rules derived from the doctor schedules (A25) - replaces the "Friday is closed" assumption.</summary>
public static class AppointmentsSchedule
{
    /// <summary>
    /// A day is off when doctors exist and none of them works on it. With no roster nothing is declared closed, so
    /// an appointment is never visually hidden by an assumption.
    /// </summary>
    public static bool IsOffDay(IEnumerable<AppointmentDoctor> doctors, DateOnly day)
    {
        var any = false;
        foreach (var d in doctors)
        {
            any = true;
            if (d.WorksOn(day)) return false;
        }

        return any;
    }
}
