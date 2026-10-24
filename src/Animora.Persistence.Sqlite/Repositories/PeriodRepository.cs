using Animora.Application.Ports;
using Animora.Domain.Finance;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Repositories;

public sealed class PeriodRepository(AnimoraLocalDbContext db) : IPeriodRepository
{
    public Task<AccountingPeriod?> FindAsync(string id, CancellationToken ct = default) =>
        db.Set<AccountingPeriod>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<AccountingPeriod> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"AccountingPeriod {id} not found.");

    public Task<AccountingPeriod?> FindByCodeAsync(string periodCode, CancellationToken ct = default) =>
        db.Set<AccountingPeriod>().AsTracking().FirstOrDefaultAsync(e => e.PeriodCode == periodCode, ct);

    public Task<AccountingPeriod?> FindContainingAsync(DateTimeOffset occurredUtc, CancellationToken ct = default) =>
        db.Set<AccountingPeriod>().AsTracking()
            .FirstOrDefaultAsync(e => e.StartUtc <= occurredUtc && e.EndUtc > occurredUtc, ct);

    public Task<AccountingPeriod?> FindNextOpenAsync(DateTimeOffset afterUtc, CancellationToken ct = default) =>
        db.Set<AccountingPeriod>().AsTracking().Where(e => e.StartUtc >= afterUtc && e.State == PeriodState.Open)
            .OrderBy(e => e.StartUtc).FirstOrDefaultAsync(ct);

    public void Add(AccountingPeriod entity) => db.Set<AccountingPeriod>().Add(entity);
    public void AddRange(IEnumerable<AccountingPeriod> entities) => db.Set<AccountingPeriod>().AddRange(entities);

    public async Task<AccountingPeriod> EnsureAsync(AccountingPeriod candidate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        // The local store is a single-writer desktop replica, so there is no cross-process race to guard
        // against: a plain find-or-add keeps exactly the same idempotent lazy-open semantics as the server
        // upsert (reuse a period with the same code, otherwise open it).
        if (db.Set<AccountingPeriod>().Local.FirstOrDefault(e => e.PeriodCode == candidate.PeriodCode && !e.Deleted) is { } tracked)
            return tracked;
        if (await FindByCodeAsync(candidate.PeriodCode, ct).ConfigureAwait(false) is { } existing)
            return existing;
        db.Set<AccountingPeriod>().Add(candidate);
        return candidate;
    }
}
