namespace Animora.Domain.Finance;

/// <summary>
/// One entry of a <see cref="Cheque"/>'s <b>append-only</b> history: who moved it from which state
/// to which, when and why.
/// </summary>
/// <remarks>
/// A cheque's current <see cref="Cheque.State"/> is a cache of the last entry here. The history is
/// never edited or pruned: a bounced cheque that is re-deposited keeps both the bounce and the
/// re-deposit, because the audit trail of a financial instrument is exactly its sequence of
/// transitions (A-RULE 003).
/// </remarks>
/// <param name="EventId">ULID identifying the history entry.</param>
/// <param name="FromState">The state the cheque left.</param>
/// <param name="ToState">The state it entered.</param>
/// <param name="OccurredUtc">When the transition happened, UTC (from the injected clock).</param>
/// <param name="ActorUserId">The staff member who made it happen.</param>
/// <param name="Reason">Why — mandatory for a bounce, a return and a cancellation.</param>
public sealed record ChequeEvent(
    string EventId,
    ChequeState FromState,
    ChequeState ToState,
    DateTimeOffset OccurredUtc,
    string ActorUserId,
    string? Reason);
