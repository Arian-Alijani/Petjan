namespace Animora.Domain.Events;

/// <summary>
/// An appointment was booked. B01+/C02 consume it to project the calendar; the slot-conflict
/// check has already passed before this is raised.
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="AppointmentId">The booked appointment.</param>
/// <param name="PetId">The patient scheduled.</param>
/// <param name="OwnerId">The patient's owner.</param>
/// <param name="DoctorUserId">The assigned doctor, or <see langword="null"/> when triaged at reception.</param>
/// <param name="StartUtc">Slot start, UTC.</param>
/// <param name="DurationMinutes">Slot length in minutes.</param>
public sealed record AppointmentBookedEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string AppointmentId,
    string PetId,
    string OwnerId,
    string? DoctorUserId,
    DateTimeOffset StartUtc,
    int DurationMinutes)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>
/// An appointment was moved to a new slot. Carries both slots so a calendar projection can
/// relocate the entry without a second read.
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="AppointmentId">The moved appointment.</param>
/// <param name="OldStartUtc">The previous slot start, UTC.</param>
/// <param name="NewStartUtc">The new slot start, UTC.</param>
/// <param name="NewDurationMinutes">The new slot length in minutes.</param>
public sealed record AppointmentMovedEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string AppointmentId,
    DateTimeOffset OldStartUtc,
    DateTimeOffset NewStartUtc,
    int NewDurationMinutes)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>
/// An appointment was cancelled. Frees its slot for the conflict policy and lets reminder
/// projections drop it.
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="AppointmentId">The cancelled appointment.</param>
/// <param name="Reason">Why it was cancelled; kept for the audit trail.</param>
public sealed record AppointmentCancelledEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string AppointmentId,
    string Reason)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>
/// A reception-queue entry was created — a patient is present. Only raised for a present patient
/// (check-in or walk-in); future appointments never create a queue row (C02 relies on this).
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="QueueEntryId">The new queue entry.</param>
/// <param name="PetId">The present patient.</param>
/// <param name="OwnerId">The patient's owner.</param>
/// <param name="AppointmentId">The appointment this grew out of, or <see langword="null"/> for a walk-in.</param>
public sealed record QueueEntryCreatedEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string QueueEntryId,
    string PetId,
    string OwnerId,
    string? AppointmentId)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);
