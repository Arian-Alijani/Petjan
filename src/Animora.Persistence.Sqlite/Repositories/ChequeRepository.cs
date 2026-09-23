using Animora.Application.Ports;
using Animora.Domain.Finance;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class ChequeRepository(AnimoraLocalDbContext db) : IChequeRepository
{
    public Task<Cheque?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<Cheque>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<Cheque> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"Cheque {id} not found.");

    public void Add(Cheque entity) => db.Set<Cheque>().Add(entity);
    public void AddRange(IEnumerable<Cheque> entities) => db.Set<Cheque>().AddRange(entities);
}
