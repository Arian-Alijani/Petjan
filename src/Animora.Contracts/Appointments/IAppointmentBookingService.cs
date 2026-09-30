namespace Animora.Contracts.Appointments;

/// <summary>
/// Data seam for the appointments detail drawer + booking sheet (Phase 14).
/// Mirrors the reference's drawer + booking controller in
/// Animora-UI/assets/appointments-panel.js: it reads a single appointment's
/// detail (<c>detailHtml</c>), resolves the patient pick-list and the
/// doctor/day slot grid (<c>freeSlots</c>), reports day/doctor load
/// (<c>conflicts</c> / the doctor busy tally) and creates / edits / cancels /
/// admits appointments. The desktop ViewModel depends only on this interface;
/// the real client service dispatches journaled commands (C04).
/// </summary>
public interface IAppointmentBookingService
{
    /// <summary>
    /// The detail of one appointment, or null when unknown (the reference's
    /// <c>S.get(id)</c> + <c>detailHtml</c>).
    /// </summary>
    Task<AppointmentDetail?> GetDetailAsync(string appointmentId);

    /// <summary>
    /// Searches persisted patient/owner keys. Requires at least two characters;
    /// returns at most 21 rows (20 visible plus one overflow indicator).
    /// </summary>
    Task<IReadOnlyList<BookingPatient>> SearchPatientsAsync(
        string search, string speciesKey, CancellationToken ct = default);

    /// <summary>Loads only the patient linked to an appointment for editing.</summary>
    Task<BookingPatient?> GetAppointmentPatientAsync(string appointmentId);

    /// <summary>
    /// Registers a new patient via the panel's quick-add form (the reference's
    /// <c>savePet()</c>) and returns it.
    /// </summary>
    Task<BookingPatient> CreatePatientAsync(NewPatientRequest request);

    /// <summary>
    /// The candidate start slots for a doctor on a date at a given duration,
    /// mirroring the reference's <c>freeSlots(dateKey, docId, dur)</c>.
    /// </summary>
    Task<IReadOnlyList<TimeSlot>> GetAvailableSlotsAsync(string doctorId, DateOnly date, int durationMinutes);

    /// <summary>
    /// Overlapping appointments for a doctor/date against a proposed range,
    /// mirroring the reference's <c>conflicts()</c>. <paramref name="exceptId"/>
    /// excludes the appointment being edited.
    /// </summary>
    Task<IReadOnlyList<AppointmentEvent>> GetConflictsAsync(
        DateOnly date, string doctorId, int startMinutes, int durationMinutes, string? exceptId);

    /// <summary>
    /// Appointment counts per non-cancelled day across an inclusive range — the
    /// mini calendar's per-day dots (the reference's <c>calInner</c> tally).
    /// </summary>
    Task<IReadOnlyDictionary<DateOnly, int>> GetDayCountsAsync(DateOnly start, DateOnly end);

    /// <summary>
    /// The number of non-cancelled appointments a doctor has on a date — the
    /// doctor grid's «نوبت» tally (the reference's <c>busy</c>).
    /// </summary>
    Task<int> GetDoctorBusyCountAsync(string doctorId, DateOnly date);

    /// <summary>
    /// Creates a new appointment (the reference's new-booking branch in
    /// <c>submitBook()</c>) and returns the persisted event.
    /// </summary>
    Task<AppointmentEvent> CreateAsync(NewAppointmentRequest request);

    /// <summary>
    /// Updates an existing appointment (the reference's edit branch in
    /// <c>submitBook()</c>) and returns the persisted event.
    /// </summary>
    Task<AppointmentEvent> UpdateAsync(string appointmentId, UpdateAppointmentRequest request);

    /// <summary>Cancels an appointment (the reference's <c>act('cancel')</c>).</summary>
    Task CancelAsync(string appointmentId);

    /// <summary>Marks an appointment as admitted/done (the reference's <c>act('checkin')</c>).</summary>
    Task CheckInAsync(string appointmentId);
}
