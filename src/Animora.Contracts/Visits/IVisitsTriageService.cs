namespace Animora.Contracts.Visits;

/// <summary>
/// Data seam for the Visits triage gate (doctor rail + entry-mode workspace).
/// The desktop ViewModel depends only on this interface; the in-memory
/// <c>MockVisitsTriageService</c> is the single swappable implementation a
/// future backend replaces (R-18).
/// </summary>
public interface IVisitsTriageService
{
    Task<IReadOnlyList<DoctorRailInfo>> GetDoctorsAsync();

    Task<IReadOnlyList<BookedAppointmentRef>> GetBookedForDoctorAsync(string doctorId);

    IReadOnlyList<VisitTypeInfo> GetVisitTypes();

    /// <summary>
    /// Built-in visit types (Persian labels) merged with the clinic's custom types from
    /// Settings → Appointments, so the visit-type filter stays in sync with the appointment settings.
    /// </summary>
    Task<IReadOnlyList<VisitTypeInfo>> GetVisitTypesAsync();

    /// <summary>
    /// Server-side patient lookup for the new-visit picker. <paramref name="search"/> is matched against the pet /
    /// owner search keys; the result is bounded (most recently seen first). Callers gate on a minimum query length.
    /// </summary>
    Task<IReadOnlyList<VisitPatientRef>> GetPatientsAsync(string? search = null);

    Task<EncounterDraft> StartFromTicketAsync(string ticketId);

    Task<EncounterDraft> StartNewAsync(NewVisitRequest request);
}
