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

    Task<IReadOnlyList<VisitPatientRef>> GetPatientsAsync();

    Task<EncounterDraft> StartFromTicketAsync(string ticketId);

    Task<EncounterDraft> StartNewAsync(NewVisitRequest request);
}
