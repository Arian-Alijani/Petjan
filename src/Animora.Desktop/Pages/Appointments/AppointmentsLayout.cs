namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// Time-grid layout constants, transcribed from the reference's state block in
/// Animora-UI/assets/appointments.js (<c>H0 = 7, H1 = 22, PXH = 78, SNAP = 15</c>)
/// plus the CSS (appointments.css <c>.tg-hour { height:78px }</c>). Keeping the
/// pixel-per-hour constant in one place lets event cards, the gutter hour rows
/// and the "now" line all agree on the same minute→pixel mapping.
/// </summary>
public static class AppointmentsLayout
{
    /// <summary>First visible hour (07:00) as minutes from midnight.</summary>
    public const int DayStartMinutes = 7 * 60;

    /// <summary>Last visible hour (22:00) as minutes from midnight.</summary>
    public const int DayEndMinutes = 22 * 60;

    /// <summary>Pixels per hour row (reference's PXH).</summary>
    public const double PixelsPerHour = 78;

    /// <summary>Minimum event-card height in px (reference's 26px floor).</summary>
    public const double MinEventHeight = 26;

    /// <summary>Number of hour rows in the grid (07:00 → 21:00).</summary>
    public const int HourCount = 15;

    /// <summary>Converts a minute span to pixels at <see cref="PixelsPerHour"/>.</summary>
    public static double MinuteToPx(int minutes) => minutes / 60.0 * PixelsPerHour;

    /// <summary>Total grid body height in px.</summary>
    public static double TotalHeight => PixelsPerHour * HourCount;
}
