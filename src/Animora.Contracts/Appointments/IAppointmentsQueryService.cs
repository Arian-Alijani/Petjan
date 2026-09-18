namespace Animora.Contracts.Appointments;

/// <summary>
/// Data seam for the Appointments calendar (day/week/month grids + the
/// «نوبت‌های پیش رو» queue and «تاریخچه» archive). The desktop ViewModel
/// depends only on this interface; the in-memory
/// <c>MockAppointmentsQueryService</c> is the single swappable implementation
/// a future backend replaces (R-18).
/// </summary>
public interface IAppointmentsQueryService
{
    /// <summary>
    /// The doctor roster (the reference's <c>DOCTORS</c>) the calendar columns
    /// and doctor filter chips are built from.
    /// </summary>
    Task<IReadOnlyList<AppointmentDoctor>> GetDoctorsAsync();

    /// <summary>
    /// The appointment-type catalog (the reference's <c>TYPES</c>) the type
    /// filter chips, event-card accents and legends are built from.
    /// </summary>
    Task<IReadOnlyList<AppointmentType>> GetTypesAsync();

    /// <summary>
    /// Every appointment within an inclusive date range, mirroring the
    /// reference's <c>onDate()</c>/<c>scopeDates()</c> lookups over its
    /// <c>APPTS</c> collection.
    /// </summary>
    Task<IReadOnlyList<AppointmentEvent>> GetForRangeAsync(DateOnly start, DateOnly end);

    /// <summary>
    /// The upcoming appointments for the queue tab, mirroring the reference's
    /// day-filtered <c>APPTS.filter(...)</c> pass in <c>renderQueue</c>.
    /// </summary>
    Task<IReadOnlyList<AppointmentEvent>> GetQueueAsync();

    /// <summary>
    /// The past appointments for the history tab, mirroring the reference's
    /// <c>histList()</c> (date ≤ today, done/cancel for today).
    /// </summary>
    Task<IReadOnlyList<AppointmentEvent>> GetHistoryAsync();
}
