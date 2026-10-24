using Animora.Domain.Finance;

namespace Animora.Application.Ports;

public interface ILedgerRepository
{
    Task<JournalEntry?> FindAsync(string id, CancellationToken ct = default);
    Task<JournalEntry> RequireAsync(string id, CancellationToken ct = default);
    Task<JournalEntry?> FindInvoicePostingAsync(string invoiceId, CancellationToken ct = default);
    void Add(JournalEntry entity);
    void AddRange(IEnumerable<JournalEntry> entities);

    /// <summary>
    /// The journal entries staged in the current unit of work that still carry
    /// <see cref="JournalEntry.PendingNumber"/> — i.e. built during this transaction's event dispatch and not
    /// yet stamped with their gapless sequential number. Returned in a deterministic post order (accounting
    /// instant, then entry id) so the deferred finalize pass assigns numbers reproducibly and gaplessly,
    /// independent of hash/enumeration order. Materialized; safe to enumerate while finalizing.
    /// </summary>
    IReadOnlyList<JournalEntry> PendingEntries();
}
