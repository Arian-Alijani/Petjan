using Animora.Domain.Finance;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>Per-period, per-account ledger totals in minor units.</summary>
/// <param name="PeriodId">Accounting period id.</param>
/// <param name="AccountCode">Posted account code.</param>
/// <param name="Currency">Line currency.</param>
/// <param name="DebitMinor">Σ debit amounts.</param>
/// <param name="CreditMinor">Σ credit amounts.</param>
/// <param name="BalanceMinor"><c>Debit − Credit</c>.</param>
public sealed record LedgerBalance(
    string PeriodId, string AccountCode, string Currency, long DebitMinor, long CreditMinor, long BalanceMinor);

/// <summary>Derives account balances from append-only journal lines.</summary>
public sealed class LedgerBalanceQuery(AnimoraLocalDbContext db)
{
    /// <summary>Maximum period ids accepted per call.</summary>
    public const int MaxPeriodIds = 60;

    /// <summary>
    /// One grouped SQL over tenant-filtered <c>JournalEntries ⨝ JournalLines</c> by
    /// <c>(PeriodId, AccountCode, AmountCurrency)</c>, summing <c>AmountMinor</c> split by <c>Side</c>.
    /// </summary>
    /// <param name="periodIds">Periods to include (1…<see cref="MaxPeriodIds"/>, distinct).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One row per period/account/currency, ordered by period then account.</returns>
    public async Task<IReadOnlyList<LedgerBalance>> GetAsync(
        IReadOnlyCollection<string> periodIds, CancellationToken ct = default)
    {
        var ids = QueryGuards.Ids(periodIds, MaxPeriodIds, nameof(periodIds));
        var rows = await db.Set<JournalEntry>().AsNoTracking()
            .Where(e => ids.Contains(e.PeriodId))
            .SelectMany(e => e.Lines, (e, l) => new
            {
                e.PeriodId, l.AccountCode, l.Amount.Currency,
                Debit = l.Side == EntrySide.Debit ? l.Amount.Minor : 0L,
                Credit = l.Side == EntrySide.Credit ? l.Amount.Minor : 0L,
            })
            .GroupBy(x => new { x.PeriodId, x.AccountCode, x.Currency })
            .Select(g => new { g.Key.PeriodId, g.Key.AccountCode, g.Key.Currency, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
            .OrderBy(x => x.PeriodId).ThenBy(x => x.AccountCode).ThenBy(x => x.Currency)
            .ToListAsync(ct);
        return rows.Select(r => new LedgerBalance(r.PeriodId, r.AccountCode, r.Currency, r.Debit, r.Credit,
            checked(r.Debit - r.Credit))).ToArray();
    }
}
