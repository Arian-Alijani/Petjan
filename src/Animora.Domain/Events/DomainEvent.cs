namespace Animora.Domain.Events;

/// <summary>
/// Base type for every in-process domain event. Deliberately free of any mediator
/// vocabulary: no <c>INotification</c>, no MediatR, no library attribute — dispatch
/// is a compile-time registered delegate map (CONVENTIONS.md §4, PERFORMANCE.md §3).
/// </summary>
/// <param name="EventId">26-character ULID from <c>IdGenerator</c>; lexicographically sortable.</param>
/// <param name="TenantId">Owning tenant of the aggregate that raised the event.</param>
/// <param name="OccurredAtUtc">
/// When the event was raised, taken from the injected clock. Informational only —
/// client wall-clock time is never a sync or licensing authority (A-RULE 011).
/// </param>
public abstract record DomainEvent(string EventId, string TenantId, DateTimeOffset OccurredAtUtc);
