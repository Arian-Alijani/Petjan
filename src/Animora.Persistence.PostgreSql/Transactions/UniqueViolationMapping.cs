namespace Animora.Persistence.PostgreSql.Transactions;

/// <summary>
/// Opt-in control for how a PostgreSQL <c>23505</c> (unique_violation) is surfaced by
/// <c>ServerUnitOfWork</c>. By default (<see cref="None"/>) a unique violation is <b>not</b> translated —
/// it is a data/logic conflict the handler should have prevented, so it propagates and fails the request.
/// A handler that treats a specific unique index as an optimistic-concurrency signal opts in with
/// <see cref="Enabled"/>, and the unit of work then returns
/// <c>Result.Fail(ErrorCodes.ConcurrencyConflict, "unique")</c> instead.
/// </summary>
public readonly record struct UniqueViolationMapping(bool MapToConcurrencyConflict)
{
    /// <summary>Do not map <c>23505</c> (the default): let the unique violation propagate.</summary>
    public static readonly UniqueViolationMapping None = new(false);

    /// <summary>Map <c>23505</c> to <c>ErrorCodes.ConcurrencyConflict</c> with detail <c>"unique"</c>.</summary>
    public static readonly UniqueViolationMapping Enabled = new(true);
}
