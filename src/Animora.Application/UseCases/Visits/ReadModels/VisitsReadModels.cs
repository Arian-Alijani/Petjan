namespace Animora.Application.UseCases.Visits.ReadModels;

/// <summary>
/// One doctor tile on the triage left rail — the three live counts and the in-visit flag are
/// computed in one projection over today's appointments, never per doctor (C02, PERFORMANCE §6).
/// </summary>
public sealed record DoctorRailReadModel(
    string Id,
    string Name,
    string Specialty,
    int BookedCount,
    bool HasVisitInProgress,
    int? NextFreeSlotMinute);

/// <summary>
/// One booked-appointment row for a doctor's list for the local day (C02). <see cref="TicketId"/>
/// is the composite reception id; <see cref="State"/> is the appointment/queue state name mapping
/// converts to the Contracts <c>BookedAppointmentState</c>.
/// </summary>
public sealed record BookedAppointmentReadModel(
    string TicketId,
    int AtMinutes,
    int DurationMinutes,
    string PetId,
    string PetName,
    string PetKind,
    string OwnerName,
    string OwnerPhone,
    string TypeId,
    bool IsUrgent,
    string State);

/// <summary>
/// A recently-seen patient offered for a new walk-in visit — top 50 by last-visit desc, reusing
/// C01's <c>SearchKey</c> index (C02).
/// </summary>
public sealed record TriagePatientReadModel(
    string PetId,
    string PetName,
    string PetKind,
    string OwnerId,
    string OwnerName,
    string OwnerPhone);

/// <summary>
/// The clinical-encounter draft returned when a visit is started from a ticket or a new-visit
/// request (C02). Carries the identity/context the encounter wizard (C03) renders; the vitals /
/// notes / billable lines are owned by <c>IVisitEncounterService</c> (C03).
/// </summary>
public sealed record EncounterDraftReadModel(
    string VisitId,
    string TicketId,
    string DoctorId,
    string DoctorName,
    string DoctorSpecialty,
    string PetId,
    string PetName,
    string PetKind,
    string OwnerName,
    string OwnerPhone,
    string TypeId,
    bool IsUrgent,
    bool IsSynthetic,
    int AtMinutes,
    int DurationMinutes,
    string Note,
    string SourceTicketId);
