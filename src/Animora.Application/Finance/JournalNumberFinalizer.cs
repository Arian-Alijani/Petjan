using Animora.Application.Ports;
using Animora.Domain.Finance;
using Animora.Domain.Results;

namespace Animora.Application.Finance;

/// <summary>
/// The single, deferred gapless-numbering pass for the ledger. See <see cref="IJournalNumberFinalizer"/> for
/// why allocation is deferred to the end of the transaction.
/// </summary>
/// <remarks>
/// <para>
/// Every <see cref="LedgerPostingHandler"/> write builds its entry with the reserved
/// <see cref="JournalEntry.PendingNumber"/> and leaves it un-numbered. This pass collects those staged entries
/// in a deterministic post order (<see cref="ILedgerRepository.PendingEntries"/> — accounting instant, then
/// time-derived id) and, per entry, allocates the next tenant/period-scoped sequence value and stamps it via the
/// one-shot <see cref="JournalEntry.FinalizeEntryNumber"/> mutator.
/// </para>
/// <para>
/// Ordering by the accounting instant first means the serialized counter advances in <b>post order</b>, so an
/// entry's sequence number and its chronological position agree — the invariant the unique
/// <c>(TenantId, EntryNumber)</c> index and auditors both rely on. Because the whole pass runs inside the
/// caller's transaction, the counter is only advanced on commit: a rollback leaves no gap.
/// </para>
/// </remarks>
public sealed class JournalNumberFinalizer(ILedgerRepository ledger, IJournalNumberAllocator numbers)
    : IJournalNumberFinalizer
{
    public async Task<Result> FinalizeAsync(CancellationToken ct = default)
    {
        // Materialized, deterministic order; empty for any command that posted nothing financial.
        var pending = ledger.PendingEntries();
        for (var i = 0; i < pending.Count; i++)
        {
            var entry = pending[i];
            // Allocate in post order so sequence order matches chronological order. The allocator's row-lock on
            // the "journal" counter is taken here, at the tail of the transaction, and released at the imminent
            // commit — the whole point of the deferral.
            var number = await numbers.AllocateAsync(entry.TenantId, entry.PeriodId, ct).ConfigureAwait(false);
            var stamped = entry.FinalizeEntryNumber(number);
            if (stamped.IsFailure) return stamped;
        }
        return Result.Ok();
    }
}
