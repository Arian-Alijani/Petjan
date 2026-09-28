using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Time;
using Animora.Domain.Finance;
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

    public Task<PagedResult<ChequeRow>> GetListAsync(int page, int size, CancellationToken ct) =>
        db.Set<Cheque>().AsNoTracking().OrderByDescending(c => c.IssuedUtc).ThenByDescending(c => c.Id)
            .Select(c => new ChequeRow(c.Id, c.Direction, c.ChequeNumber, c.SayadId, c.BankName,
                c.BankBranch, c.Direction == ChequeDirection.Inbound ? c.OwnerId ?? "" : c.SupplierId ?? "",
                c.Amount.Minor, c.IssuedUtc, c.DueUtc, c.State, c.InvoiceId, c.AccountNumber, c.Note))
            .ToPagedAsync(page, size, options.Value, ct);

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
