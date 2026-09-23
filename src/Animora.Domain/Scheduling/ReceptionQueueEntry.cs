using Animora.Domain.Events;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Domain.Scheduling;

/// <summary>Lifecycle state of a reception-queue entry.</summary>
public enum QueueState
{
    /// <summary>Present and waiting to be called.</summary>
    Waiting = 0,

    /// <summary>Called by a doctor.</summary>
    Called = 1,

    /// <summary>In the examination room; a visit exists.</summary>
    InVisit = 2,

    /// <summary>Seen and closed. Terminal.</summary>
    Done = 3,

    /// <summary>Left before being seen. Terminal.</summary>
    Left = 4,
}

/// <summary>How the patient entered the queue.</summary>
public enum QueueSource
{
    /// <summary>Checked in against a booked appointment.</summary>
    Appointment = 0,

    /// <summary>Registered on the spot without an appointment.</summary>
    WalkIn = 1,
}

/// <summary>
/// A reception-queue entry: one <b>present</b> patient waiting to be seen.
/// </summary>
/// <remarks>
/// <para>
/// <b>[DECISION] The queue is derived, not duplicated.</b> An entry exists only for a patient who
/// is physically present — it is created at appointment check-in or at walk-in registration.
/// Future appointments never create queue rows; the queue is today's waiting room, not a copy of
/// the calendar (C02 depends on this). This keeps the queue small and its ordering meaningful.
/// </para>
/// <para>
/// <see cref="TicketNumber"/> is a per-tenant, per-local-day counter assigned by the Application
/// layer (B01+), not by this entity — the domain has no notion of "local day" or a shared counter.
/// It is set once at creation and never changes here.
/// </para>
/// <para>
/// The state machine is the only authority over transitions (CONVENTIONS.md §6 item 9):
/// </para>
/// <code>
/// Waiting -> Called | Left
/// Called  -> InVisit | Waiting | Left
/// InVisit -> Done
/// Done / Left -> (terminal)
/// </code>
/// </remarks>
public sealed class ReceptionQueueEntry : AggregateRoot
{
    /// <summary>Lowest priority — a normal patient.</summary>
    public const int PriorityNormal = 0;

    /// <summary>Highest priority — an emergency.</summary>
    public const int PriorityEmergency = 2;

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private ReceptionQueueEntry()
    {
    }

    /// <summary>The present patient.</summary>
    public string PetId { get; private set; } = string.Empty;

    /// <summary>The patient's owner, denormalized so the queue never needs a second read.</summary>
    public string OwnerId { get; private set; } = string.Empty;

    /// <summary>The appointment this grew out of, or <see langword="null"/> for a walk-in.</summary>
    public string? AppointmentId { get; private set; }

    /// <summary>The doctor who called the patient; <see langword="null"/> until called.</summary>
    public string? DoctorUserId { get; private set; }

    /// <summary>Current lifecycle state.</summary>
    public QueueState State { get; private set; } = QueueState.Waiting;

    /// <summary>How the patient entered the queue.</summary>
    public QueueSource Source { get; private set; }

    /// <summary>Per-tenant, per-local-day ticket number assigned by the Application layer (B01+).</summary>
    public int TicketNumber { get; private set; }

    /// <summary>When the patient joined the queue, UTC.</summary>
    public DateTimeOffset EnqueuedUtc { get; private set; }

    /// <summary>When the patient was called, UTC; <see langword="null"/> until then.</summary>
    public DateTimeOffset? CalledUtc { get; private set; }

    /// <summary>When the entry closed, UTC; <see langword="null"/> until done or left.</summary>
    public DateTimeOffset? ClosedUtc { get; private set; }

    /// <summary>Triage priority: 0 normal, 1 urgent, 2 emergency.</summary>
    public int Priority { get; private set; }

    /// <summary>Free-form reception note.</summary>
    public string? Note { get; private set; }

    /// <summary>
    /// The visit this entry turned into (A04); set by <see cref="StartVisit"/>.
    /// </summary>
    public string? VisitId { get; private set; }

    /// <summary>
    /// Creates a queue entry for a <b>present</b> patient — at appointment check-in or walk-in
    /// registration only. <paramref name="ticketNumber"/> is supplied by the Application layer.
    /// Raises <see cref="QueueEntryCreatedEvent"/>.
    /// </summary>
    public static ReceptionQueueEntry Create(
        string tenantId,
        string petId,
        string ownerId,
        QueueSource source,
        int ticketNumber,
        IClock clock,
        string? appointmentId = null,
        int priority = PriorityNormal,
        string? note = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(petId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.UtcNow;
        var entry = new ReceptionQueueEntry
        {
            Id = IdGenerator.NewId(now),
            TenantId = tenantId,
            PetId = petId,
            OwnerId = ownerId,
            AppointmentId = string.IsNullOrWhiteSpace(appointmentId) ? null : appointmentId,
            Source = source,
            TicketNumber = ticketNumber,
            State = QueueState.Waiting,
            EnqueuedUtc = now,
            Priority = ClampPriority(priority),
            Note = Clean(note),
        };

        entry.Raise(new QueueEntryCreatedEvent(
            IdGenerator.NewId(now), tenantId, now, entry.Id, petId, ownerId, entry.AppointmentId));
        return entry;
    }

    /// <summary>Calls the patient, recording the doctor and <see cref="CalledUtc"/>.</summary>
    public Result Call(string doctorUserId, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(doctorUserId);
        ArgumentNullException.ThrowIfNull(clock);

        var moved = Transition(QueueState.Called);
        if (moved.IsFailure)
        {
            return moved;
        }

        DoctorUserId = doctorUserId;
        CalledUtc = clock.UtcNow;
        return Result.Ok();
    }

    /// <summary>Sends a called patient into the examination room, linking the visit.</summary>
    public Result StartVisit(string visitId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(visitId);

        var moved = Transition(QueueState.InVisit);
        if (moved.IsFailure)
        {
            return moved;
        }

        VisitId = visitId;
        return Result.Ok();
    }

    /// <summary>Closes the entry as seen, stamping <see cref="ClosedUtc"/>.</summary>
    public Result Close(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var moved = Transition(QueueState.Done);
        if (moved.IsFailure)
        {
            return moved;
        }

        ClosedUtc = clock.UtcNow;
        return Result.Ok();
    }

    /// <summary>Records that the patient left before being seen, stamping <see cref="ClosedUtc"/>.</summary>
    public Result MarkLeft(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var moved = Transition(QueueState.Left);
        if (moved.IsFailure)
        {
            return moved;
        }

        ClosedUtc = clock.UtcNow;
        return Result.Ok();
    }

    /// <summary>Re-triages the patient. Legal while the entry is still open.</summary>
    public Result SetPriority(int priority)
    {
        if (State is QueueState.Done or QueueState.Left)
        {
            return Result.Fail(
                ErrorCodes.QueueInvalidTransition,
                $"Queue entry '{Id}' is {State}; priority is fixed.");
        }

        Priority = ClampPriority(priority);
        return Result.Ok();
    }

    /// <summary><see langword="true"/> when the tabled machine allows <paramref name="from"/> → <paramref name="to"/>.</summary>
    public static bool IsTransitionAllowed(QueueState from, QueueState to) => (from, to) switch
    {
        (QueueState.Waiting, QueueState.Called) => true,
        (QueueState.Waiting, QueueState.Left) => true,
        (QueueState.Called, QueueState.InVisit) => true,
        (QueueState.Called, QueueState.Waiting) => true,
        (QueueState.Called, QueueState.Left) => true,
        (QueueState.InVisit, QueueState.Done) => true,
        _ => false,
    };

    /// <summary>The single gate every state change goes through.</summary>
    private Result Transition(QueueState target)
    {
        if (!IsTransitionAllowed(State, target))
        {
            return Result.Fail(
                ErrorCodes.QueueInvalidTransition,
                $"Queue entry '{Id}' cannot move {State} -> {target}.");
        }

        State = target;
        return Result.Ok();
    }

    private static int ClampPriority(int priority) =>
        Math.Clamp(priority, PriorityNormal, PriorityEmergency);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
