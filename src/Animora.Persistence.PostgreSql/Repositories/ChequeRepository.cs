using Animora.Application.Ports;
using Animora.Domain.Finance;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class ChequeRepository(AnimoraServerDbContext db) : TenantRepository<Cheque>(db), IChequeRepository
{
    public Task<Cheque?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<Cheque>().Local.FirstOrDefault(e => e.Id == id && !e.Deleted) is { } local
            ? Task.FromResult<Cheque?>(local) : db.Set<Cheque>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<Cheque> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"Cheque {id} not found.");

    public new void Add(Cheque entity) => db.Set<Cheque>().Add(entity);
    public void AddRange(IEnumerable<Cheque> entities) => db.Set<Cheque>().AddRange(entities);
}
