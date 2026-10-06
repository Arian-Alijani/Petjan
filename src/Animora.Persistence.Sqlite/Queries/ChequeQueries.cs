using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Time;
using Animora.Application.Text;
using Animora.Domain.Finance;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Purchasing;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>Bounded projections; date buckets use the clinic calendar, not SQLite date functions.</summary>
public sealed class ChequeQueries(AnimoraLocalDbContext db, IOptions<LocalStoreOptions> options) : IChequeQueries
{
    private static readonly TimeZoneInfo ClinicZone = ResolveZone();

    private static TimeZoneInfo ResolveZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone("Iran", TimeSpan.FromHours(3.5), "Iran", "Iran"); }
    }

    private const int LookupLimit = 50;

    public async Task<PagedResult<ChequeRow>> GetListAsync(ChequeListCriteria criteria, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        // Counterparty names are resolved in the same projection (left joins), so the list never shows raw ids.
        var rows =
            from c in db.Set<Cheque>().AsNoTracking()
            join o in db.Set<Owner>().AsNoTracking() on c.OwnerId equals o.Id into owners
            from o in owners.DefaultIfEmpty()
            join s in db.Set<Supplier>().AsNoTracking() on c.SupplierId equals s.Id into suppliers
            from s in suppliers.DefaultIfEmpty()
            select new
            {
                c.Id, c.Direction, c.ChequeNumber, c.SayadId, c.BankName, c.BankBranch, c.State,
                c.IssuedUtc, c.DueUtc, c.InvoiceId, c.AccountNumber, c.Note, Amount = c.Amount.Minor,
                Party = c.Direction == ChequeDirection.Inbound
                    ? (o == null ? "" : o.FullName)
                    : (s == null ? "" : s.Name),
            };

        if (criteria.Direction is { } direction) rows = rows.Where(r => r.Direction == direction);
        rows = criteria.Scope switch
        {
            ChequeListScope.Open => rows.Where(r => r.State == ChequeState.Registered
                || r.State == ChequeState.InPortfolio || r.State == ChequeState.Deposited),
            ChequeListScope.Attention => rows.Where(r => r.State == ChequeState.Bounced),
            ChequeListScope.Closed => rows.Where(r => r.State == ChequeState.Cleared || r.State == ChequeState.Returned
                || r.State == ChequeState.Cancelled || r.State == ChequeState.Settled),
            _ => rows,
        };
        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var term = "%" + PersianText.NormalizeDigits(criteria.Search.Trim()) + "%";
            rows = rows.Where(r => EF.Functions.Like(r.ChequeNumber, term)
                || (r.SayadId != null && EF.Functions.Like(r.SayadId, term))
                || EF.Functions.Like(r.Party, term));
        }

        // Work still in flight is read by what is due first; finished cheques by what closed most recently.
        var ordered = criteria.Scope is ChequeListScope.Open or ChequeListScope.Attention
            ? rows.OrderBy(r => r.DueUtc).ThenBy(r => r.Id)
            : rows.OrderByDescending(r => r.DueUtc).ThenByDescending(r => r.Id);
        return await ordered
            .Select(r => new ChequeRow(r.Id, r.Direction, r.ChequeNumber, r.SayadId, r.BankName, r.BankBranch, r.Party,
                r.Amount, r.IssuedUtc, r.DueUtc, r.State, r.InvoiceId, r.AccountNumber, r.Note))
            .ToPagedAsync(Math.Max(1, criteria.Page), criteria.PageSize, options.Value, ct);
    }

    public async Task<ChequeDetailRow?> GetDetailAsync(string id, CancellationToken ct)
    {
        var cheque = await db.Set<Cheque>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cheque is null) return null;

        string party;
        if (cheque.Direction == ChequeDirection.Inbound && cheque.OwnerId is { } ownerId)
            party = await db.Set<Owner>().AsNoTracking().Where(o => o.Id == ownerId).Select(o => o.FullName)
                .FirstOrDefaultAsync(ct) ?? string.Empty;
        else if (cheque.SupplierId is { } supplierId)
            party = await db.Set<Supplier>().AsNoTracking().Where(s => s.Id == supplierId).Select(s => s.Name)
                .FirstOrDefaultAsync(ct) ?? string.Empty;
        else
            party = string.Empty;

        var actorIds = cheque.History.Select(h => h.ActorUserId).Distinct().ToArray();
        var actors = actorIds.Length == 0
            ? new Dictionary<string, string>()
            : await db.Set<User>().AsNoTracking().Where(u => actorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var row = new ChequeRow(cheque.Id, cheque.Direction, cheque.ChequeNumber, cheque.SayadId, cheque.BankName,
            cheque.BankBranch, party, cheque.Amount.Minor, cheque.IssuedUtc, cheque.DueUtc, cheque.State,
            cheque.InvoiceId, cheque.AccountNumber, cheque.Note);
        var history = cheque.History.OrderByDescending(h => h.OccurredUtc).ThenByDescending(h => h.EventId)
            .Select(h => new ChequeHistoryRow(h.EventId, h.FromState, h.ToState, h.OccurredUtc,
                actors.GetValueOrDefault(h.ActorUserId, string.Empty), h.Reason)).ToArray();
        return new ChequeDetailRow(row, history);
    }

    public async Task<ChequeSummaryRow> GetSummaryAsync(CancellationToken ct)
    {
        var groups = await db.Set<Cheque>().AsNoTracking()
            .GroupBy(c => new { c.Direction, c.State })
            .Select(g => new { g.Key.Direction, g.Key.State, Count = g.Count(), Sum = g.Sum(c => c.Amount.Minor) })
            .ToListAsync(ct);
        long inbound = 0, outbound = 0, bouncedSum = 0;
        int inboundCount = 0, outboundCount = 0, bouncedCount = 0;
        foreach (var g in groups)
        {
            if (g.State == ChequeState.Bounced)
            {
                bouncedSum += g.Sum;
                bouncedCount += g.Count;
            }
            else if (g.State is ChequeState.Registered or ChequeState.InPortfolio or ChequeState.Deposited)
            {
                if (g.Direction == ChequeDirection.Inbound) { inbound += g.Sum; inboundCount += g.Count; }
                else { outbound += g.Sum; outboundCount += g.Count; }
            }
        }

        return new ChequeSummaryRow(inbound, inboundCount, outbound, outboundCount, bouncedSum, bouncedCount);
    }

    public async Task<IReadOnlyList<ChequePartyRow>> FindOwnersAsync(string? search, CancellationToken ct)
    {
        var q = db.Set<Owner>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = PersianText.Normalize(search);
            q = q.Where(o => EF.Functions.Like(o.SearchKey, "%" + term + "%"));
        }

        return await q.OrderBy(o => o.FullName).ThenBy(o => o.Id).Take(LookupLimit)
            .Select(o => new ChequePartyRow(o.Id, o.FullName, o.PhonePrimary)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ChequePartyRow>> FindSuppliersAsync(string? search, CancellationToken ct)
    {
        var q = db.Set<Supplier>().AsNoTracking().Where(s => s.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = "%" + PersianText.NormalizeDigits(search.Trim()) + "%";
            q = q.Where(s => EF.Functions.Like(s.Name, term) || EF.Functions.Like(s.Code, term)
                || (s.Phone != null && EF.Functions.Like(s.Phone, term)));
        }

        return await q.OrderBy(s => s.Name).ThenBy(s => s.Id).Take(LookupLimit)
            .Select(s => new ChequePartyRow(s.Id, s.Name, s.Phone)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ChequeCashflowWeek>> GetCashflowAsync(int weeks, DateOnly today, CancellationToken ct)
    {
        weeks = Math.Clamp(weeks, 1, 8);
        var monday = today.AddDays(-((int)today.DayOfWeek + 6) % 7);
        var from = new DateTimeOffset(ClinicCalendar.ToUtcTicks(monday, 0, ClinicZone), TimeSpan.Zero);
        var to = new DateTimeOffset(ClinicCalendar.ToUtcTicks(monday.AddDays(weeks * 7), 0, ClinicZone), TimeSpan.Zero);
        var rows = await db.Set<Cheque>().AsNoTracking()
            .Where(c => c.DueUtc >= from && c.DueUtc < to &&
                (c.State == ChequeState.Registered || c.State == ChequeState.InPortfolio ||
                 c.State == ChequeState.Deposited || c.State == ChequeState.Bounced))
            .Select(c => new { c.DueUtc, c.Direction, Amount = c.Amount.Minor }).ToListAsync(ct);
        var totals = new long[weeks, 2];
        foreach (var row in rows)
        {
            var day = ClinicCalendar.LocalDay(row.DueUtc.UtcTicks, ClinicZone);
            var index = (day.DayNumber - monday.DayNumber) / 7;
            if ((uint)index < (uint)weeks) totals[index, row.Direction == ChequeDirection.Inbound ? 0 : 1] += row.Amount;
        }
        return Enumerable.Range(0, weeks).Select(i =>
            new ChequeCashflowWeek(monday.AddDays(7 * i), totals[i, 0], totals[i, 1])).ToArray();
    }

    public async Task<IReadOnlyList<ChequeBankRow>> GetBankBreakdownAsync(CancellationToken ct) =>
        await db.Set<Cheque>().AsNoTracking().Where(c => c.BankName != null)
            .GroupBy(c => c.BankName!).Select(g => new ChequeBankRow(g.Key, g.Sum(c => c.Amount.Minor), g.Count()))
            .OrderByDescending(b => b.AmountMinor).ToListAsync(ct);

    public async Task<IReadOnlyList<ChequeAlertRow>> GetAlertsAsync(DateOnly today, CancellationToken ct)
    {
        var lower = new DateTimeOffset(ClinicCalendar.ToUtcTicks(today.AddDays(-90), 0, ClinicZone), TimeSpan.Zero);
        var upper = new DateTimeOffset(ClinicCalendar.ToUtcTicks(today.AddDays(ChequeAlertRules.DueWithinDays + 1), 0, ClinicZone), TimeSpan.Zero);
        var rows = await db.Set<Cheque>().AsNoTracking()
            .Where(c => c.State == ChequeState.Bounced ||
                (c.DueUtc >= lower && c.DueUtc < upper &&
                 (c.State == ChequeState.Registered || c.State == ChequeState.InPortfolio || c.State == ChequeState.Deposited)))
            .OrderBy(c => c.DueUtc).Take(200)
            .Select(c => new { c.Id, c.State, c.DueUtc, Amount = c.Amount.Minor }).ToListAsync(ct);
        return rows.Select(c =>
        {
            var day = ClinicCalendar.LocalDay(c.DueUtc.UtcTicks, ClinicZone);
            var kind = ChequeAlertRules.BouncedUnresolved(c.State) ? "Bounced" :
                ChequeAlertRules.OverdueUncleared(c.State, day, today) ? "Overdue" :
                ChequeAlertRules.DueWithin3Days(c.State, day, today) ? "DueSoon" : null;
            return kind is null ? null : new ChequeAlertRow(c.Id, kind, day, c.Amount);
        }).OfType<ChequeAlertRow>().ToArray();
    }
}
