namespace Animora.Contracts.Appointments;

/// <summary>
/// The status of an appointment, mirroring the reference's <c>STATUS</c> table
/// (Animora-UI/assets/appointments.js): <c>confirmed | pending | done | cancel</c>.
/// Drives the status pill, the event-card flag dot and the history filters.
/// </summary>
public enum AppointmentStatus
{
    Confirmed,
    Pending,
    Done,
    Cancelled,
}

/// <summary>
/// The full appointment lifecycle, 1:1 with the domain state machine (A04). <see cref="AppointmentStatus"/> is a
/// coarse legacy grouping; every label, count and action gate must use this instead so check-in, in-visit,
/// completed and no-show are never conflated.
/// </summary>
public enum AppointmentLifecycle
{
    Scheduled,
    Confirmed,
    CheckedIn,
    InVisit,
    Completed,
    NoShow,
    Cancelled,
}

/// <summary>
/// The invoice state of a past appointment, mirroring the reference's
/// <c>invoice</c> field (<c>paid | unpaid | partial | null</c>) that the
/// history row's financial column renders (<c>INV_META</c>).
/// </summary>
public enum AppointmentInvoiceState
{
    None,
    Paid,
    Unpaid,
    Partial,
}

/// <summary>
/// The payment method of a settled appointment, mirroring the reference's
/// <c>pay</c> field (<c>cash | card | online | null</c>) rendered by
/// <c>PAY_META</c>.
/// </summary>
public enum AppointmentPaymentMethod
{
    None,
    Cash,
    Card,
    Online,
}

/// <summary>
/// The clinical outcome of a past appointment, mirroring the reference's
/// <c>visit</c> field (<c>complete | no-show | refunded | null</c>) rendered by
/// <c>VISIT_META</c>.
/// </summary>
public enum AppointmentVisitOutcome
{
    None,
    Complete,
    NoShow,
    Refunded,
}

/// <summary>
/// One appointment on the calendar, mirroring the fields the reference's
/// <c>evCard</c> / queue card / history row read from its <c>APPTS</c>
/// collection: patient + owner identity, the doctor and appointment type
/// (by id, resolved through <c>AppointmentCatalog</c>), the start time as
/// minutes-from-midnight plus a duration, the day as a real
/// <see cref="DateOnly"/> (the seam a future backend fills), and the optional
/// financial/visit outcome fields that only past/done rows carry.
/// </summary>
public sealed record AppointmentEvent(
    string Id,
    DateOnly Date,
    int StartMinutes,
    int DurationMinutes,
    string TypeId,
    string DoctorId,
    string PetName,
    string PetKind,
    string OwnerName,
    string OwnerPhone,
    string AvatarTone,
    string SpeciesIconKey,
    AppointmentStatus Status,
    string Note,
    string Code,
    int Total,
    AppointmentInvoiceState Invoice,
    AppointmentPaymentMethod Payment,
    AppointmentVisitOutcome Visit,
    AppointmentLifecycle Lifecycle = AppointmentLifecycle.Scheduled,
    string? VisitId = null,
    DateTimeOffset? CheckedInUtc = null,
    string? CancelReason = null,
    string CreatedByUserId = "",
    int RescheduleCount = 0)
{
    /// <summary>Start time as minutes from midnight (reference's <c>a.start</c>).</summary>
    public int EndMinutes => StartMinutes + DurationMinutes;

    /// <summary>The patient has arrived (checked in, in visit or finished). A no-show never counts.</summary>
    public bool IsCheckedIn => Lifecycle is AppointmentLifecycle.CheckedIn or AppointmentLifecycle.InVisit
        or AppointmentLifecycle.Completed;

    /// <summary>Terminal states: nothing may change any more.</summary>
    public bool IsTerminal => Lifecycle is AppointmentLifecycle.Completed or AppointmentLifecycle.NoShow
        or AppointmentLifecycle.Cancelled;

    /// <summary>Mirrors the domain table: only Scheduled/Confirmed can be checked in.</summary>
    public bool CanCheckIn => Lifecycle is AppointmentLifecycle.Scheduled or AppointmentLifecycle.Confirmed;

    /// <summary>Mirrors the move handler: anything before the visit starts.</summary>
    public bool CanMove => Lifecycle < AppointmentLifecycle.InVisit;

    /// <summary>Mirrors the cancel handler: a checked-in appointment is rejected by the backend.</summary>
    public bool CanCancel => Lifecycle is AppointmentLifecycle.Scheduled or AppointmentLifecycle.Confirmed
        or AppointmentLifecycle.InVisit;

    /// <summary>A linked visit exists to open.</summary>
    public bool CanOpenVisit => !string.IsNullOrEmpty(VisitId) &&
        Lifecycle is AppointmentLifecycle.InVisit or AppointmentLifecycle.Completed;
}

/// <summary>
/// A doctor in the clinic roster, mirroring a row of the reference's
/// <c>DOCTORS</c> table: display + short names, the specialty subtitle, the
/// avatar initial and the per-doctor accent color.
/// </summary>
public sealed record AppointmentDoctor(
    string Id,
    string Name,
    string ShortName,
    string Specialty,
    string Initial,
    string Accent);

/// <summary>
/// An appointment type, mirroring a row of the reference's <c>TYPES</c> table:
/// the label, the CSS class key (<c>visit</c>/<c>vaccine</c>/…), the accent /
/// background / foreground palette and the default duration in minutes.
/// </summary>
public sealed record AppointmentType(
    string Id,
    string Label,
    string ClassKey,
    string Accent,
    string Background,
    string Foreground,
    string IconKey,
    int DurationMinutes);
