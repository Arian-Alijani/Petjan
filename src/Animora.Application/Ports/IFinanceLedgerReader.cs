using Animora.Contracts.Wire.Sync;

namespace Animora.Application.Ports;

/// <summary>
/// D04 — the authoritative financial read path. Returns a tenant's invoices/payments that changed at or
/// after a timestamp cursor so a peer device can display cross-device transactions the generic state feed
/// deliberately excludes (command-settled money is never replicated as feed state). Tenant-scoped by the
/// server's global query filters; strictly read-only — nothing here crosses back into the command pipeline.
/// </summary>
public interface IFinanceLedgerReader
{
    /// <summary>
    /// The window of invoices/payments whose effective last-change time is ≥ <paramref name="sinceTicks"/> (UTC ticks,
    /// 0 for a first full sync), newest-change first, bounded by <paramref name="maxRows"/> per collection.
    /// </summary>
    Task<FinanceReadPage> GetLedgerSinceAsync(long sinceTicks, int maxRows, CancellationToken ct);
}
