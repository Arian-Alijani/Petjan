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

    public void Add(JournalEntry entity) => db.Set<JournalEntry>().Add(entity);
    public void AddRange(IEnumerable<JournalEntry> entities) => db.Set<JournalEntry>().AddRange(entities);
}
