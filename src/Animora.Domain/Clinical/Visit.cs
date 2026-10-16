using Animora.Domain.Events;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Domain.Clinical;

/// <summary>Lifecycle state of an encounter.</summary>
public enum VisitState
{
    /// <summary>Created, nothing recorded yet.</summary>
    Draft = 0,

    /// <summary>The doctor has started the examination.</summary>
    InProgress = 1,

    /// <summary>Clinically finished; a diagnosis exists.</summary>
    Completed = 2,

    /// <summary>Handed off to Sales; the billable lines are final.</summary>
    ReadyForInvoice = 3,

    /// <summary>An invoice was issued for this encounter. Terminal.</summary>
    Invoiced = 4,

    /// <summary>Abandoned before completion. Terminal.</summary>
    Cancelled = 5,
}

/// <summary>Why the patient was presented.</summary>
public enum VisitReason
{
    /// <summary>Routine health check.</summary>
    Checkup = 0,

    /// <summary>Presented sick.</summary>
    Illness = 1,

    /// <summary>Trauma.</summary>
    Injury = 2,

    /// <summary>Vaccination appointment.</summary>
    Vaccination = 3,

    /// <summary>Surgical procedure.</summary>
    Surgery = 4,

    /// <summary>Dental procedure.</summary>
    Dental = 5,

    /// <summary>Follow-up on an earlier encounter.</summary>
    Followup = 6,

    /// <summary>Anything else; the chief complaint carries the detail.</summary>
    Other = 7,
}

/// <summary>
/// The encounter aggregate root: one visit of one patient, from triage to invoice handoff.
/// </summary>
/// <remarks>
/// <para>
/// The state machine below is the <b>only</b> authority over what may change and when
/// (<c>CONVENTIONS.md</c> §6 item 9 — the domain decides, a version mismatch never does):
/// </para>
/// <code>
/// Draft           -> InProgress | Cancelled
/// InProgress      -> Completed  | Cancelled
/// Completed       -> ReadyForInvoice
/// ReadyForInvoice -> Invoiced
/// Invoiced        -> (terminal)
/// Cancelled       -> (terminal)
/// </code>
/// <para>
/// Every mutator returns <see cref="Result"/>: an out-of-order click is expected user behaviour,
/// not a defect. Billable and prescription lines freeze at <see cref="VisitState.Invoiced"/> and
/// above, because Sales has already turned them into a financial document.
/// </para>
/// </remarks>
public sealed class Visit : AggregateRoot
{
    private readonly List<PrescriptionLine> _prescriptions = [];
    private readonly List<VisitServiceLine> _serviceLines = [];
    private readonly List<RadiologyRecord> _radiology = [];
    private readonly List<LabResult> _labResults = [];

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private Visit()
    {
    }

    /// <summary>The patient seen.</summary>
    public string PetId { get; private set; } = string.Empty;

    /// <summary>The patient's owner, denormalized so billing never needs a second read.</summary>
    public string OwnerId { get; private set; } = string.Empty;

    /// <summary>The appointment this encounter grew out of, when there was one (A05).</summary>
    public string? AppointmentId { get; private set; }

    /// <summary>The attending doctor.</summary>
    public string DoctorUserId { get; private set; } = string.Empty;

    /// <summary>Current lifecycle state.</summary>
    public VisitState State { get; private set; } = VisitState.Draft;

    /// <summary>Why the patient was presented.</summary>
    public VisitReason Reason { get; private set; }

    /// <summary>Presenting complaint in the owner's words.</summary>
    public string? ChiefComplaint { get; private set; }

    /// <summary>When the encounter record was opened, UTC.</summary>
    public DateTimeOffset StartedUtc { get; private set; }

    /// <summary>When the encounter was clinically completed, UTC.</summary>
    public DateTimeOffset? CompletedUtc { get; private set; }

    /// <summary>Triage vitals; <see langword="null"/> until recorded.</summary>
    public PetVitals? Vitals { get; private set; }

    /// <summary>The doctor's diagnosis. Required before <see cref="Complete"/>.</summary>
    public string? Diagnosis { get; private set; }

    /// <summary>The treatment plan handed to the owner.</summary>
    public string? TreatmentPlan { get; private set; }

    /// <summary>Internal clinical notes.</summary>
    public string? DoctorNotes { get; private set; }

    /// <summary>The invoice raised for this encounter (A07); set by <see cref="AttachInvoice"/>.</summary>
    public string? InvoiceId { get; private set; }

    /// <summary>Why the encounter was cancelled; kept for the audit trail.</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>Prescribed drugs.</summary>
    public IReadOnlyList<PrescriptionLine> Prescriptions => _prescriptions;

    /// <summary>Billable lines — the payload Sales converts into invoice lines.</summary>
    public IReadOnlyList<VisitServiceLine> ServiceLines => _serviceLines;

    /// <summary>Radiology studies recorded during the encounter.</summary>
    public IReadOnlyList<RadiologyRecord> Radiology => _radiology;

    /// <summary>Laboratory result sheets recorded during the encounter.</summary>
    public IReadOnlyList<LabResult> LabResults => _labResults;

    /// <summary><see langword="true"/> once the lines may no longer change.</summary>
    public bool AreLinesLocked => State >= VisitState.Invoiced;

    /// <summary>Opens an encounter in <see cref="VisitState.Draft"/>. <paramref name="id"/> is the deterministic id derived from the
    /// creating command's operation (<see cref="IdGenerator.ForOperation"/>) so client and server agree; <see langword="null"/> mints a fresh ULID.</summary>
    public static Visit Open(
        string tenantId,
        string petId,
        string ownerId,
        string doctorUserId,
        VisitReason reason,
        IClock clock,
        string? appointmentId = null,
        string? chiefComplaint = null,
        string? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(petId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(doctorUserId);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.UtcNow;
        return new Visit
        {
            Id = string.IsNullOrWhiteSpace(id) ? IdGenerator.NewId(now) : id,
            TenantId = tenantId,
            PetId = petId,
            OwnerId = ownerId,
            DoctorUserId = doctorUserId,
            Reason = reason,
            AppointmentId = string.IsNullOrWhiteSpace(appointmentId) ? null : appointmentId,
            ChiefComplaint = Clean(chiefComplaint),
            State = VisitState.Draft,
            StartedUtc = now,
        };
    }

    /// <summary>Moves a draft encounter into examination.</summary>
    public Result Start() => Transition(VisitState.InProgress);

    /// <summary>Records the triage vitals. Refused once the encounter is closed.</summary>
    public Result SetVitals(PetVitals vitals)
    {
        ArgumentNullException.ThrowIfNull(vitals);

        var guard = EnsureOpen();
        if (guard.IsFailure)
        {
            return guard;
        }

        Vitals = vitals;
        return Result.Ok();
    }

    /// <summary>Records diagnosis, plan and notes. Refused once the encounter is closed.</summary>
    public Result SetAssessment(string? diagnosis, string? treatmentPlan = null, string? doctorNotes = null)
    {
        var guard = EnsureOpen();
        if (guard.IsFailure)
        {
            return guard;
        }

        Diagnosis = Clean(diagnosis);
        TreatmentPlan = Clean(treatmentPlan);
        DoctorNotes = Clean(doctorNotes);
        return Result.Ok();
    }

    /// <summary>Adds a prescribed drug.</summary>
    public Result AddPrescription(PrescriptionLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var guard = EnsureLinesEditable();
        if (guard.IsFailure)
        {
            return guard;
        }

        _prescriptions.Add(line);
        return Result.Ok();
    }

    /// <summary>Removes a prescribed drug by its line id.</summary>
    public Result RemovePrescription(string lineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        return RemoveLine(_prescriptions, static l => l.LineId, lineId);
    }

    /// <summary>Adds a billable line.</summary>
    public Result AddServiceLine(VisitServiceLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var guard = EnsureLinesEditable();
        if (guard.IsFailure)
        {
            return guard;
        }

        _serviceLines.Add(line);
        return Result.Ok();
    }

    /// <summary>Removes a billable line by its line id.</summary>
    public Result RemoveServiceLine(string lineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        return RemoveLine(_serviceLines, static l => l.LineId, lineId);
    }

    /// <summary>Records a radiology study. Refused once the encounter is closed or when the id already exists.</summary>
    public Result AddRadiology(RadiologyRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return AddClinicalRecord(_radiology, record, static r => r.Id);
    }

    /// <summary>Removes a radiology study by its id. Refused once the encounter is closed.</summary>
    public Result RemoveRadiology(string radiologyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(radiologyId);
        return RemoveClinicalRecord(_radiology, static r => r.Id, radiologyId);
    }

    /// <summary>Records a laboratory result sheet. Refused once the encounter is closed or when the id already exists.</summary>
    public Result AddLabResult(LabResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return AddClinicalRecord(_labResults, result, static r => r.Id);
    }

    /// <summary>Removes a laboratory result sheet by its id. Refused once the encounter is closed.</summary>
    public Result RemoveLabResult(string labResultId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(labResultId);
        return RemoveClinicalRecord(_labResults, static r => r.Id, labResultId);
    }

    /// <summary>
    /// Closes the encounter clinically. A diagnosis is mandatory — an encounter without one is an
    /// incomplete medical record, so this returns <c>VISIT_DIAGNOSIS_REQUIRED</c>.
    /// Raises <see cref="VisitCompletedEvent"/>.
    /// </summary>
    public Result Complete(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (string.IsNullOrWhiteSpace(Diagnosis))
        {
            return Result.Fail(ErrorCodes.VisitDiagnosisRequired, $"Visit '{Id}' has no diagnosis.");
        }

        var moved = Transition(VisitState.Completed);
        if (moved.IsFailure)
        {
            return moved;
        }

        var now = clock.UtcNow;
        CompletedUtc = now;
        Raise(new VisitCompletedEvent(IdGenerator.NewId(now), TenantId, now, Id, PetId, OwnerId, DoctorUserId));
        return Result.Ok();
    }

    /// <summary>Hands the encounter off to Sales. Raises <see cref="VisitReadyForInvoiceEvent"/>.</summary>
    public Result MarkReadyForInvoice()
    {
        var moved = Transition(VisitState.ReadyForInvoice);
        if (moved.IsFailure)
        {
            return moved;
        }

        var at = CompletedUtc ?? StartedUtc;
        Raise(new VisitReadyForInvoiceEvent(IdGenerator.NewId(at), TenantId, at, Id, PetId, OwnerId));
        return Result.Ok();
    }

    /// <summary>
    /// Links the issued invoice and moves to <see cref="VisitState.Invoiced"/>. Only legal from
    /// <see cref="VisitState.ReadyForInvoice"/>: an invoice may never be attached to an encounter
    /// Sales has not been handed.
    /// </summary>
    public Result AttachInvoice(string invoiceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceId);

        var moved = Transition(VisitState.Invoiced);
        if (moved.IsFailure)
        {
            return moved;
        }

        InvoiceId = invoiceId;
        return Result.Ok();
    }

    /// <summary>Abandons the encounter. Legal only before it is clinically completed.</summary>
    public Result Cancel(string reason, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(clock);

        var moved = Transition(VisitState.Cancelled);
        if (moved.IsFailure)
        {
            return moved;
        }

        CancellationReason = reason.Trim();
        CompletedUtc ??= clock.UtcNow;
        return Result.Ok();
    }

    /// <summary><see langword="true"/> when the tabled machine allows <paramref name="from"/> → <paramref name="to"/>.</summary>
    public static bool IsTransitionAllowed(VisitState from, VisitState to) => (from, to) switch
    {
        (VisitState.Draft, VisitState.InProgress) => true,
        (VisitState.Draft, VisitState.Cancelled) => true,
        (VisitState.InProgress, VisitState.Completed) => true,
        (VisitState.InProgress, VisitState.Cancelled) => true,
        (VisitState.Completed, VisitState.ReadyForInvoice) => true,
        (VisitState.ReadyForInvoice, VisitState.Invoiced) => true,
        _ => false,
    };

    /// <summary>The single gate every state change goes through.</summary>
    private Result Transition(VisitState target)
    {
        if (!IsTransitionAllowed(State, target))
        {
            return Result.Fail(
                ErrorCodes.VisitInvalidTransition,
                $"Visit '{Id}' cannot move {State} -> {target}.");
        }

        State = target;
        return Result.Ok();
    }

    private Result EnsureOpen() =>
        State is VisitState.Draft or VisitState.InProgress
            ? Result.Ok()
            : Result.Fail(ErrorCodes.VisitInvalidTransition, $"Visit '{Id}' is {State}; clinical data is closed.");

    private Result EnsureLinesEditable() =>
        AreLinesLocked
            ? Result.Fail(ErrorCodes.VisitLocked, $"Visit '{Id}' is {State}; its lines are final.")
            : Result.Ok();

    private Result AddClinicalRecord<T>(List<T> records, T record, Func<T, string> idOf)
    {
        var guard = EnsureOpen();
        if (guard.IsFailure)
        {
            return guard;
        }

        var id = idOf(record);
        if (records.Exists(r => string.Equals(idOf(r), id, StringComparison.Ordinal)))
        {
            return Result.Fail(ErrorCodes.ValidationFailed, $"Record '{id}' already exists on visit '{Id}'.");
        }

        records.Add(record);
        return Result.Ok();
    }

    private Result RemoveClinicalRecord<T>(List<T> records, Func<T, string> idOf, string recordId)
    {
        var guard = EnsureOpen();
        if (guard.IsFailure)
        {
            return guard;
        }

        var index = records.FindIndex(r => string.Equals(idOf(r), recordId, StringComparison.Ordinal));
        if (index < 0)
        {
            return Result.Fail(ErrorCodes.NotFound, $"Record '{recordId}' is not on visit '{Id}'.");
        }

        records.RemoveAt(index);
        return Result.Ok();
    }

    private Result RemoveLine<T>(List<T> lines, Func<T, string> idOf, string lineId)
    {
        var guard = EnsureLinesEditable();
        if (guard.IsFailure)
        {
            return guard;
        }

        var index = lines.FindIndex(l => string.Equals(idOf(l), lineId, StringComparison.Ordinal));
        if (index < 0)
        {
            return Result.Fail(ErrorCodes.NotFound, $"Line '{lineId}' is not on visit '{Id}'.");
        }

        lines.RemoveAt(index);
        return Result.Ok();
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
