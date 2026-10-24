using Animora.Domain.Finance;

namespace Animora.Application.Ports;

public interface IPeriodRepository
{
    Task<AccountingPeriod?> FindAsync(string id, CancellationToken ct = default);
    Task<AccountingPeriod> RequireAsync(string id, CancellationToken ct = default);
    Task<AccountingPeriod?> FindByCodeAsync(string periodCode, CancellationToken ct = default);
    Task<AccountingPeriod?> FindContainingAsync(DateTimeOffset occurredUtc, CancellationToken ct = default);
    Task<AccountingPeriod?> FindNextOpenAsync(DateTimeOffset afterUtc, CancellationToken ct = default);
    void Add(AccountingPeriod entity);
    void AddRange(IEnumerable<AccountingPeriod> entities);

    /// <summary>
    /// Atomically opens the period described by <paramref name="candidate"/> if — and only if — no row
    /// with the same <c>(TenantId, PeriodCode)</c> exists yet, then returns the authoritative row (the one
    /// this call created, or the one a concurrent post already created). Concurrency-safe by construction:
    /// the server implementation is a single <c>INSERT … ON CONFLICT (TenantId, PeriodCode) DO NOTHING</c>
    /// upsert, so two first-posts into a brand-new month can no longer race into an un-retried
    /// unique-violation (<c>23505</c>). This is the race-free replacement for the pessimistic clinic-wide
    /// ledger lock that previously serialised every financial command just to protect the once-per-month
    /// lazy open (it mirrors the proven <c>DocumentCounterAllocator</c> upsert). The returned entity is
    /// tracked so the caller may post against it. <c>DO NOTHING</c> (not <c>DO UPDATE</c>) guarantees an
    /// existing open/closed period is never silently mutated here.
    /// </summary>
    Task<AccountingPeriod> EnsureAsync(AccountingPeriod candidate, CancellationToken ct = default);
}
