using Animora.Application.Ports;
using Animora.Domain.Finance;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class LedgerRepository(AnimoraServerDbContext db) : TenantRepository<JournalEntry>(db), ILedgerRepository
{
    public Task<JournalEntry?> FindAsync(string id, CancellationToken ct = default) =>
        Context.Set<JournalEntry>().Local.FirstOrDefault(e => e.Id == id && !e.Deleted) is { } local
            ? Task.FromResult<JournalEntry?>(local) : Context.Set<JournalEntry>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<JournalEntry> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"JournalEntry {id} not found.");

    public Task<JournalEntry?> FindInvoicePostingAsync(string invoiceId, CancellationToken ct = default) =>
        Context.Set<JournalEntry>().AsNoTracking()
            .FirstOrDefaultAsync(e => e.SourceEntityType == "Invoice" && e.SourceEntityId == invoiceId &&
                e.Source == JournalSource.Sales, ct);

    public new void Add(JournalEntry entity) => Context.Set<JournalEntry>().Add(entity);
    public void AddRange(IEnumerable<JournalEntry> entities) => Context.Set<JournalEntry>().AddRange(entities);
}
