namespace Animora.Domain.Events;

/// <summary>
/// A boarding stay was activated — the pet has been admitted to its room. Billable days begin
/// accruing from <see cref="Boarding.BoardingStay.CheckInUtc"/>.
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="StayId">The activated stay.</param>
/// <param name="PetId">The boarded patient.</param>
/// <param name="OwnerId">The patient's owner.</param>
/// <param name="RoomId">The room the pet occupies.</param>
public sealed record StayActivatedEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string StayId,
    string PetId,
    string OwnerId,
    string RoomId)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>
/// A stay was handed off to Sales. Its <c>Items</c> plus <c>DailyRate × BillableDays</c> are the
/// invoice payload (A07); the stay never computes totals itself.
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="StayId">The stay to bill.</param>
/// <param name="PetId">The boarded patient.</param>
/// <param name="OwnerId">The party to invoice.</param>
/// <param name="BillableDays">Full days to charge at <see cref="Boarding.BoardingStay.DailyRate"/>.</param>
public sealed record StayReadyForInvoiceEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string StayId,
    string PetId,
    string OwnerId,
    int BillableDays)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>
/// A boarding pet was transferred between rooms. Carries both rooms so an occupancy projection can
/// update without a second read.
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="StayId">The transferred stay.</param>
/// <param name="FromRoomId">The room vacated.</param>
/// <param name="ToRoomId">The room occupied.</param>
/// <param name="Reason">Why the transfer happened; kept for the audit trail.</param>
public sealed record StayRoomTransferredEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string StayId,
    string FromRoomId,
    string ToRoomId,
    string Reason)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>
/// A stay was checked out. Only raised after an invoice was attached — a stay is never checked
/// out unbilled.
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="StayId">The checked-out stay.</param>
/// <param name="PetId">The boarded patient.</param>
/// <param name="RoomId">The room freed.</param>
/// <param name="ActualCheckOutUtc">When the pet actually left, UTC.</param>
public sealed record StayCheckedOutEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string StayId,
    string PetId,
    string RoomId,
    DateTimeOffset ActualCheckOutUtc)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);
