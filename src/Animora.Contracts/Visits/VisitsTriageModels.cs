namespace Animora.Contracts.Visits;

/// <summary>
/// Which entry path the operator picked on the triage gate, mirroring the
/// reference's <c>S.mode</c> (<c>'appt' | 'new'</c>) in
/// Animora-UI/assets/visit.js.
/// </summary>
public enum VisitEntryMode
{
    Booked,
    NewVisit,
}

/// <summary>
/// Visit type catalog entry, mirroring a row of the reference's <c>TYPES</c>
/// array (Animora-UI/assets/visit.js): id, Persian label, icon key, accent
/// color and the default duration used to derive the queue slot length.
/// </summary>
public sealed record VisitTypeInfo(
    string Id,
    string Label,
    string IconKey,
    string Accent,
    int DefaultDurationMinutes);

/// <summary>
/// Triage state of a single booked appointment ticket, mirroring the
/// reference's per-row state badge (<c>waiting | called | inVisit</c> map to
/// «آمادهٔ ویزیت»/«فراخوانده شده»/«در ویزیت»).
/// </summary>
public enum BookedAppointmentState
{
    Waiting,
    Called,
    InVisit,
}

/// <summary>
/// One doctor tile on the left rail, mirroring the fields the reference's
/// <c>renderDocRail</c> reads from <c>CLINIC.DOCTORS</c> plus the two live
/// counts <c>docApptCount</c>/<c>docWalkinCount</c> derive.
/// </summary>
public sealed record DoctorRailInfo(
    string Id,
    string Name,
    string Specialty,
    string Initials,
    string Accent,
    int AppointmentCount,
    int WalkInCount,
    bool HasVisitInProgress = false);

/// <summary>
/// One row of a doctor's booked-appointment list for today — the shape
/// <c>apptRowHtml</c> renders (time slot, duration, patient identity, type
/// swatch, urgency and state).
/// </summary>
public sealed record BookedAppointmentRef(
    string TicketId,
    int AtMinutes,
    int DurationMinutes,
    string PatientName,
    string PatientKind,
    string PatientIconKey,
    string OwnerName,
    string OwnerPhone,
    string TypeId,
    bool IsUrgent,
    BookedAppointmentState State);

/// <summary>
/// A patient that can be picked for a new walk-in visit — mirrors the
/// deduped pet proxy the reference builds from its queue (<c>petsFromQueue</c>).
/// </summary>
public sealed record VisitPatientRef(
    string Id,
    string Name,
    string Kind,
    string OwnerName,
    string Phone,
    string IconKey);

/// <summary>
/// New-visit form payload, mirroring the reference's <c>S.nb</c> builder
/// state (patient + type + priority + note) plus the doctor the visit is
/// started under (the reference reads <c>S.docId</c> separately when building
/// the synthetic ticket).
/// </summary>
public sealed record NewVisitRequest(
    string DoctorId,
    string PatientName,
    string PatientKind,
    string OwnerName,
    string OwnerPhone,
    string PatientIconKey,
    bool IsNewPatient,
    string TypeId,
    bool IsUrgent,
    string Note,
    string? PatientId = null);

/// <summary>
/// The clinical encounter draft handed to the wizard (Phase 11), mirroring
/// the object <c>buildEncounter</c> produces plus the ticket/doctor context
/// the wizard's ribbon and patient sidebar render (patient identity, owner
/// contact, the doctor lock-up, the queue slot time/duration and whether the
/// visit came from a booked ticket or a walk-in). The four vital readings,
/// history/exam/diagnosis/plan text and the billable lines are held separately
/// by <c>IVisitEncounterService</c> so this record stays the encounter's
/// identity/context — the mock is the single swap point for a future backend
/// (R-18).
/// </summary>
public sealed record EncounterDraft(
    string Id,
    string TicketId,
    string DoctorId,
    string DoctorName,
    string DoctorShortName,
    string DoctorSpecialty,
    string DoctorInitials,
    string DoctorAccent,
    string PatientName,
    string PatientKind,
    string PatientIconKey,
    string OwnerName,
    string OwnerPhone,
    string TypeId,
    string TypeLabel,
    string TypeAccent,
    bool IsUrgent,
    bool IsSynthetic,
    int AtMinutes,
    int DurationMinutes,
    string Note,
    string SourceTicketId);
