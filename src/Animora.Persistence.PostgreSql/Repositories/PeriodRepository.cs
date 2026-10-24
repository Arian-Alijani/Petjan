using Animora.Application.Ports;
using Animora.Domain.Finance;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Animora.Persistence.PostgreSql.Repositories;

public sealed class PeriodRepository(AnimoraServerDbContext db) : TenantRepository<AccountingPeriod>(db), IPeriodRepository
{
    public Task<AccountingPeriod?> FindAsync(string id, CancellationToken ct = default) =>
        Context.Set<AccountingPeriod>().Local.FirstOrDefault(e => e.Id == id && !e.Deleted) is { } local
            ? Task.FromResult<AccountingPeriod?>(local) : Context.Set<AccountingPeriod>().AsTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<AccountingPeriod> RequireAsync(string id, CancellationToken ct = default) =>
        await FindAsync(id, ct) ?? throw new InvalidOperationException($"AccountingPeriod {id} not found.");

    public Task<AccountingPeriod?> FindByCodeAsync(string periodCode, CancellationToken ct = default) =>
        Context.Set<AccountingPeriod>().AsTracking().FirstOrDefaultAsync(e => e.PeriodCode == periodCode, ct);

    public Task<AccountingPeriod?> FindContainingAsync(DateTimeOffset occurredUtc, CancellationToken ct = default) =>
        Context.Set<AccountingPeriod>().AsTracking()
            .FirstOrDefaultAsync(e => e.StartUtc <= occurredUtc && e.EndUtc > occurredUtc, ct);

    public Task<AccountingPeriod?> FindNextOpenAsync(DateTimeOffset afterUtc, CancellationToken ct = default) =>
        Context.Set<AccountingPeriod>().AsTracking().Where(e => e.StartUtc >= afterUtc && e.State == PeriodState.Open)
            .OrderBy(e => e.StartUtc).FirstOrDefaultAsync(ct);

    public new void Add(AccountingPeriod entity) => Context.Set<AccountingPeriod>().Add(entity);
    public void AddRange(IEnumerable<AccountingPeriod> entities) => Context.Set<AccountingPeriod>().AddRange(entities);

    public async Task<AccountingPeriod> EnsureAsync(AccountingPeriod candidate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!string.Equals(candidate.TenantId, Context.CurrentTenantId, StringComparison.Ordinal))
            throw new InvalidOperationException("Period tenant must match the authenticated tenant.");
        var transaction = Context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Lazy period open requires the command transaction.");

        // Already materialised in this tracker (e.g. an earlier post in the same command already opened it) —
        // reuse it and skip the DB round-trip entirely.
        if (Context.Set<AccountingPeriod>().Local.FirstOrDefault(e => e.PeriodCode == candidate.PeriodCode && !e.Deleted) is { } tracked)
            return tracked;

        // Atomic lazy-open. The unique (TenantId, PeriodCode) index makes this a single race-free upsert, so two
        // concurrent first-posts into a brand-new month converge on one row instead of one hitting an un-retried
        // 23505 (unique_violation is deliberately never retried by RetryPolicy). This is exactly why the
        // clinic-wide "ledger-period" advisory lock could be dropped from the hot path. Id/TenantId/PeriodCode/
        // StartUtc/EndUtc/State/UpdatedAt are supplied; Version (gen_random_uuid) and Deleted (false) fall to
        // their column defaults — the journal capture trigger reads the stored Version from the new row, so the
        // period is journaled/replicated identically to an EF insert. DO NOTHING (not DO UPDATE) guarantees an
        // existing open/closed period is never silently mutated and fires no spurious journal row.
        await using (var command = Context.Database.GetDbConnection().CreateCommand())
        {
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = """
                INSERT INTO public."AccountingPeriods"
                    ("Id", "TenantId", "PeriodCode", "StartUtc", "EndUtc", "State", "UpdatedAt")
                VALUES (@id, @tenant, @code, @start, @end, @state, @updated)
                ON CONFLICT ("TenantId", "PeriodCode") DO NOTHING;
                """;
            foreach (var (name, value) in new (string Name, object Value)[]
            {
                ("id", candidate.Id), ("tenant", candidate.TenantId), ("code", candidate.PeriodCode),
                ("start", candidate.StartUtc), ("end", candidate.EndUtc), ("state", (int)candidate.State),
                ("updated", candidate.UpdatedAt == default ? DateTimeOffset.UtcNow : candidate.UpdatedAt),
            })
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // Re-read the authoritative row (ours, or the concurrent winner's) tracked, so the caller posts against it.
        // The INSERT above is visible to this query because both run on the same connection/transaction.
        return await FindByCodeAsync(candidate.PeriodCode, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Period '{candidate.PeriodCode}' vanished after upsert.");
    }
}
