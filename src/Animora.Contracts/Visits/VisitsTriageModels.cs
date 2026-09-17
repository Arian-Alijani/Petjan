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
    int WalkInCount);

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
/// state (patient + type + priority + note).
/// </summary>
public sealed record NewVisitRequest(
    string PatientName,
    string PatientKind,
    string OwnerName,
    string OwnerPhone,
    string PatientIconKey,
    bool IsNewPatient,
    string TypeId,
    bool IsUrgent,
    string Note);

/// <summary>
/// The clinical encounter draft handed to the wizard (Phase 11), mirroring
/// the object <c>buildEncounter</c> produces (id, ticket, patient, type,
/// priority and source ticket).
/// </summary>
public sealed record EncounterDraft(
    string Id,
    string TicketId,
    string DoctorId,
    string PatientName,
    string PatientKind,
    string OwnerName,
    string TypeId,
    bool IsUrgent,
    string Note,
    string SourceTicketId);
