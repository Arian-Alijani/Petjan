namespace Animora.Domain.Events;

/// <summary>
/// A patient was soft-deleted. Feeds the history ledger («بیمار حذف شد») with the acting user and instant.
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When the deletion happened (the replicated <c>DeletedUtc</c>).</param>
/// <param name="PetId">The deleted patient.</param>
/// <param name="OwnerId">The patient's owner.</param>
/// <param name="ActorUserId">The user who performed the deletion.</param>
public sealed record PetDeletedEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string PetId,
    string OwnerId,
    string ActorUserId)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>
/// An owner was soft-deleted (their pets are cascaded and raise their own <see cref="PetDeletedEvent"/>).
/// Feeds the history ledger («سرپرست حذف شد») with the acting user and instant.
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When the deletion happened (the replicated <c>DeletedUtc</c>).</param>
/// <param name="OwnerId">The deleted owner.</param>
/// <param name="ActorUserId">The user who performed the deletion.</param>
public sealed record OwnerDeletedEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string OwnerId,
    string ActorUserId)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);
