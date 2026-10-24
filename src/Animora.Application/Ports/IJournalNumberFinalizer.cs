using Animora.Domain.Results;

namespace Animora.Application.Ports;

/// <summary>
/// Stamps the real gapless sequential number onto every journal entry that was staged with
/// <see cref="Animora.Domain.Finance.JournalEntry.PendingNumber"/> during the current unit of work.
/// </summary>
/// <remarks>
/// Runs once, at the very end of the command transaction (after all events are dispatched and all entries are
/// staged, immediately before the save/commit) on every write path — the server financial-event processor and
/// the local unit of work. Deferring allocation to this single late pass shrinks the serialized
/// <c>DocumentCounters</c> row-lock hold window to ~the save instant, instead of the whole event-dispatch body,
/// while the allocation staying inside the same transaction keeps the counter gapless (a rollback never advances
/// it). It is a no-op when nothing financial was posted.
/// </remarks>
public interface IJournalNumberFinalizer
{
    Task<Result> FinalizeAsync(CancellationToken ct = default);
}
