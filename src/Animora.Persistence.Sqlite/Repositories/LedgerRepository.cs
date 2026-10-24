using Animora.Application.Ports;
using Animora.Domain.Finance;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class LedgerRepository(AnimoraLocalDbContext db) : ILedgerRepository
{
    public Task<JournalEntry?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<JournalEntry>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<JournalEntry> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"JournalEntry {id} not found.");

    // An invoice posted and voided inside one unit of work (e.g. the sample-data seed) has its posting entry
    // only in the change tracker, not yet in the store: look there first so the reversal finds its original.
    public Task<JournalEntry?> FindInvoicePostingAsync(string invoiceId, CancellationToken ct = default) =>
        db.Set<JournalEntry>().Local.FirstOrDefault(e => e.SourceEntityType == "Invoice" &&
            e.SourceEntityId == invoiceId && e.Source == JournalSource.Sales) is { } pending
            ? Task.FromResult<JournalEntry?>(pending)
            : db.Set<JournalEntry>().AsTracking()
                .FirstOrDefaultAsync(e => e.SourceEntityType == "Invoice" && e.SourceEntityId == invoiceId &&
                    e.Source == JournalSource.Sales, ct);

    public void Add(JournalEntry entity) => db.Set<JournalEntry>().Add(entity);
    public void AddRange(IEnumerable<JournalEntry> entities) => db.Set<JournalEntry>().AddRange(entities);

    // Read from the change tracker's Local view, not the store: these entries were just staged by the
    // event dispatch and are not yet saved. Order is deterministic (accounting instant, then time-derived id)
    // so the deferred finalize pass stamps numbers gaplessly and reproducibly regardless of enumeration order.
    public IReadOnlyList<JournalEntry> PendingEntries() =>
        db.Set<JournalEntry>().Local
            .Where(e => e.HasPendingNumber)
            .OrderBy(e => e.PostedUtc).ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToArray();
}
