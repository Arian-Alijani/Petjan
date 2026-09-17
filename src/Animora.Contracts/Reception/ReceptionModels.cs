namespace Animora.Contracts.Reception;

/// <summary>
/// Queue ticket lifecycle state, mirroring the reference's ticket state
/// machine (Animora-UI/assets/clinic-flow.js TICKET_EDGES): waiting →
/// called → inVisit → done, with left/noShow/cancelled terminal branches.
/// </summary>
public enum QueueTicketState
{
    Waiting,
    Called,
    InVisit,
    Done,
    Left,
    NoShow,
    Cancelled,
}

/// <summary>How the ticket entered the queue (reception.js SRC_META).</summary>
public enum QueueTicketSource
{
    Appointment,
    WalkIn,
    Urgent,
}

/// <summary>
/// One doctor available for assignment, mirroring the reference's fixed
/// doctor roster (Animora-UI/assets/clinic-data.js DOCTORS) — used to
/// populate the walk-in modal's doctor picker.
/// </summary>
public sealed record ReceptionDoctor(string Id, string Name, string Specialty);

/// <summary>
/// One reception queue ticket. Immutable record — mutation methods on
/// <see cref="IReceptionQueueService"/> replace the stored instance with a
/// `with`-derived copy (idiomatic C#, mirrors the reference's imperative
/// <c>qt.state = to</c> assignment without exposing mutable public state).
/// </summary>
public sealed record QueueTicket(
    string Id,
    string PatientName,
    string PatientKind,
    string OwnerName,
    string OwnerPhone,
    string DoctorId,
    string DoctorName,
    string DoctorSpecialty,
    string DoctorInitials,
    string DoctorAccent,
    QueueTicketSource Source,
    QueueTicketState State,
    bool IsUrgent,
    DateTimeOffset ArrivalTime,
    int DurationMinutes,
    int Sequence);

/// <summary>
/// Walk-in ticket creation payload — mirrors the reference's
/// <c>createWalkin({ name, owner, phone, docId, urgent })</c> params.
/// </summary>
public sealed record WalkInRequest(
    string Name,
    string Owner,
    string Phone,
    string DoctorId,
    bool IsUrgent);
