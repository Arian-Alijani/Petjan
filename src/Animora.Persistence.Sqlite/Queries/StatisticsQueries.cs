using System.Globalization;
using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Time;
using Animora.Application.UseCases.Finance.ReadModels;
using Animora.Domain.Clinical;
using Animora.Domain.Finance;
using Animora.Domain.Inventory;
using Animora.Domain.Patients;
using Animora.Domain.Purchasing;
using Animora.Domain.Sales;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>
/// «آمار» read projections. Invoice revenue, lines and clients use the shared deduplicated local/server ledger;
/// costs and expenses use <see cref="DailyFinanceFact"/>. Day windows are converted to UTC bounds once with
/// <see cref="ClinicCalendar"/> and bucketed in memory — never with SQLite date functions.
/// </summary>
public sealed class StatisticsQueries(AnimoraLocalDbContext db, IClinicTimeZoneProvider zones,
    IOptions<LocalStoreOptions> options) : IStatisticsQueries
{
    private const int MaxTake = 50;
    private static readonly PersianCalendar Jalali = new();

    private string Tenant => db.CurrentTenantId;

    public async Task<StatisticsOverviewReadModel> GetOverviewAsync(FinanceDayRange range, int todayLocalDay, CancellationToken ct)
    {
        var previous = Previous(range);
        var current = await TotalsAsync(range, ct);
        var before = await TotalsAsync(previous, ct);
        var today = range.FromDay == todayLocalDay && range.ToDay == todayLocalDay
            ? current : await TotalsAsync(new FinanceDayRange(todayLocalDay, todayLocalDay), ct);
        return new StatisticsOverviewReadModel(range, previous, current, before, today);
    }

    public async Task<IReadOnlyList<StatisticsSeriesPoint>> GetRevenueSeriesAsync(FinanceDayRange range, string bucket, CancellationToken ct)
    {
        var facts = await FinanceAnalytics.DaysAsync(db, range.FromDay, range.ToDay, zones.GetClinicTimeZone(), ct);
        var visits = await VisitDaysAsync(range, ct);
        var buckets = Buckets(range, bucket);
        var revenue = new long[buckets.Count];
        var expense = new long[buckets.Count];
        var cogs = new long[buckets.Count];
        var invoices = new int[buckets.Count];
        var visitCounts = new int[buckets.Count];
        foreach (var f in facts)
        {
            var i = IndexOf(buckets, Parse(f.Key));
            if (i < 0) continue;
            revenue[i] += f.Value.Revenue - f.Value.Discount - f.Value.Refund;
            expense[i] += f.Value.Expense;
            cogs[i] += f.Value.Cogs;
            invoices[i] += f.Value.Invoices;
        }
        foreach (var day in visits)
        {
            var i = IndexOf(buckets, day);
            if (i >= 0) visitCounts[i]++;
        }
        return buckets.Select((start, i) => new StatisticsSeriesPoint(start, revenue[i], expense[i], cogs[i],
            invoices[i], visitCounts[i])).ToArray();
    }

    public async Task<IReadOnlyList<RevenueSourceReadModel>> GetRevenueBySourceAsync(FinanceDayRange range, CancellationToken ct)
    {
        var (from, to) = UtcBounds(range);
        var lines = await FinanceAnalytics.LinesAsync(db, from, to, ct);
        var totals = new Dictionary<string, (long Amount, int Count)>(StringComparer.Ordinal)
        {
            ["visit"] = (0, 0), ["product"] = (0, 0), ["boarding"] = (0, 0), ["other"] = (0, 0),
        };
        foreach (var line in lines)
        {
            var (amount, count) = totals[line.Source];
            totals[line.Source] = (amount + line.Net, count + 1);
        }
        return totals.Select(t => new RevenueSourceReadModel(t.Key, t.Value.Amount, t.Value.Count)).ToArray();
    }

    public async Task<PagedResult<ExpenseRowReadModel>> GetExpensesAsync(FinanceDayRange range, string? categoryCode,
        int page, int pageSize, CancellationToken ct)
    {
        var expenses = Expenses(range);
        if (!string.IsNullOrWhiteSpace(categoryCode))
            expenses = expenses.Where(e => e.CategoryCode == categoryCode);
        var query = from e in expenses
            join s in db.Set<Supplier>().AsNoTracking() on e.SupplierId equals s.Id into suppliers
            from s in suppliers.DefaultIfEmpty()
            orderby e.IncurredUtc descending, e.Id descending
            select new ExpenseRowReadModel(e.Id, e.ExpenseNumber, e.CategoryCode, e.Title, e.Amount.Minor,
                e.IncurredUtc, e.State, e.SettledMethod, s == null ? null : s.Name, e.Note, e.ReversesExpenseId);
        return await query.ToPagedAsync(Math.Max(1, page), pageSize, options.Value, ct);
    }

    public async Task<ExpenseStatsReadModel> GetExpenseStatsAsync(FinanceDayRange range, string bucket, CancellationToken ct)
    {
        var current = await CategoryTotalsAsync(range, ct);
        var previous = await CategoryTotalsAsync(Previous(range), ct);
        var categories = ExpenseCategories.All
            .Select(code => new ExpenseCategoryTotalReadModel(code,
                current.GetValueOrDefault(code).Amount, previous.GetValueOrDefault(code).Amount,
                current.GetValueOrDefault(code).Count))
            .Where(c => c.AmountMinor != 0 || c.PreviousAmountMinor != 0)
            .OrderByDescending(c => c.AmountMinor).ToArray();
        var trend = await GetRevenueSeriesAsync(range, bucket, ct);
        return new ExpenseStatsReadModel(current.Values.Sum(v => v.Amount), previous.Values.Sum(v => v.Amount),
            categories, trend);
    }

    public async Task<IReadOnlyList<TopProductReadModel>> GetTopProductsAsync(FinanceDayRange range, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, MaxTake);
        var (from, to) = UtcBounds(range);
        var lines = await FinanceAnalytics.LinesAsync(db, from, to, ct);
        var top = lines.Where(l => l.Source == "product" && l.ProductId != null)
            .GroupBy(l => l.ProductId!, StringComparer.Ordinal)
            .Select(g => new { ProductId = g.Key, Revenue = g.Sum(l => l.Gross),
                Quantity = g.Sum(l => l.Quantity), Title = g.First().Title })
            .Where(g => g.Revenue > 0).OrderByDescending(g => g.Revenue)
            .ThenBy(g => g.ProductId, StringComparer.Ordinal).Take(take).ToArray();
        if (top.Length == 0) return [];
        var ids = top.Select(t => t.ProductId).ToArray();
        var costs = await db.Set<ProductSalesFact>().AsNoTracking()
            .Where(f => f.TenantId == Tenant && f.LocalDay >= range.FromDay && f.LocalDay <= range.ToDay
                && ids.Contains(f.ProductId)).GroupBy(f => f.ProductId)
            .Select(g => new { Id = g.Key, Cost = g.Sum(f => f.CogsMinor) }).ToDictionaryAsync(g => g.Id, g => g.Cost, ct);
        var names = await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Name }).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        return top.Select(t => new TopProductReadModel(t.ProductId, names.GetValueOrDefault(t.ProductId) ?? t.Title,
            t.Quantity, t.Revenue, costs.GetValueOrDefault(t.ProductId))).ToArray();
    }

    public async Task<IReadOnlyList<TopClientReadModel>> GetTopClientsAsync(int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, MaxTake);
        var top = await FinanceAnalytics.Invoices(db).Where(i => i.OwnerId != null)
            .GroupBy(i => i.OwnerId).Select(g => new { OwnerId = g.Key, Spend = g.Sum(i => i.TotalMinor),
                Count = g.Count(), Last = g.Max(i => i.PostedUtc) })
            .Where(g => g.Spend > 0).OrderByDescending(g => g.Spend).ThenBy(g => g.OwnerId).Take(take).ToListAsync(ct);
        var ids = top.Select(t => t.OwnerId).ToArray();
        var owners = await db.Set<Owner>().AsNoTracking().Where(o => ids.Contains(o.Id))
            .Select(o => new { o.Id, o.FullName, o.PhonePrimary }).ToDictionaryAsync(o => o.Id, ct);
        return top.Where(t => t.OwnerId != null && owners.ContainsKey(t.OwnerId)).Select(t =>
            new TopClientReadModel(t.OwnerId!, owners[t.OwnerId!].FullName, owners[t.OwnerId!].PhonePrimary,
                t.Spend, t.Count, t.Last)).ToArray();
    }

    public async Task<IReadOnlyList<SpeciesSliceReadModel>> GetSpeciesDistributionAsync(CancellationToken ct)
    {
        var groups = await db.Set<Pet>().AsNoTracking().Where(p => p.Status == PetStatus.Active)
            .GroupBy(p => p.Species).Select(g => new { Species = g.Key, Count = g.Count() }).ToListAsync(ct);
        return groups.OrderByDescending(g => g.Count)
            .Select(g => new SpeciesSliceReadModel(g.Species.ToString(), g.Count)).ToArray();
    }

    public async Task<ClientStatsReadModel> GetClientStatsAsync(FinanceDayRange range, CancellationToken ct)
    {
        var (from, to) = UtcBounds(range);
        var owners = db.Set<Owner>().AsNoTracking();
        var total = await owners.CountAsync(ct);
        var registered = await owners.CountAsync(o => o.RegisteredUtc >= from && o.RegisteredUtc < to, ct);
        var invoices = FinanceAnalytics.Invoices(db).Where(i => i.OwnerId != null);
        var active = invoices.Where(i => i.PostedUtc >= from && i.PostedUtc < to).Select(i => i.OwnerId).Distinct();
        var activeCount = await active.CountAsync(ct);
        var returning = await active.CountAsync(id => invoices.Any(p => p.OwnerId == id && p.PostedUtc < from), ct);
        return new ClientStatsReadModel(registered, activeCount, returning, total);
    }

    public async Task<IReadOnlyList<TopClientReadModel>> GetTopVisitorsAsync(int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, MaxTake);
        var top = await FinanceAnalytics.Invoices(db).Where(i => i.OwnerId != null)
            .GroupBy(i => i.OwnerId).Select(g => new { OwnerId = g.Key, Spend = g.Sum(i => i.TotalMinor),
                Count = g.Count(), Last = g.Max(i => i.PostedUtc) })
            .Where(g => g.Count > 0).OrderByDescending(g => g.Count).ThenByDescending(g => g.Spend)
            .ThenBy(g => g.OwnerId).Take(take).ToListAsync(ct);
        var ids = top.Select(t => t.OwnerId).ToArray();
        var owners = await db.Set<Owner>().AsNoTracking().Where(o => ids.Contains(o.Id))
            .Select(o => new { o.Id, o.FullName, o.PhonePrimary }).ToDictionaryAsync(o => o.Id, ct);
        return top.Where(t => t.OwnerId != null && owners.ContainsKey(t.OwnerId)).Select(t =>
            new TopClientReadModel(t.OwnerId!, owners[t.OwnerId!].FullName, owners[t.OwnerId!].PhonePrimary,
                t.Spend, t.Count, t.Last)).ToArray();
    }

    public async Task<IReadOnlyList<BirthdayPetReadModel>> GetTodaysBirthdaysAsync(int todayLocalDay, CancellationToken ct)
    {
        var zone = zones.GetClinicTimeZone();
        var today = Parse(todayLocalDay);
        // Pet count per clinic is bounded; project the few needed columns and match the clinic-local month/day in memory
        // (SQLite date functions are deliberately never used for the finance/patient projections — see class summary).
        var pets = await db.Set<Pet>().AsNoTracking()
            .Where(p => p.Status == PetStatus.Active && p.BirthDateUtc != null)
            .Select(p => new { p.Id, p.Name, p.Species, p.OwnerId, p.BirthDateUtc })
            .ToListAsync(ct);
        var matches = pets
            .Select(p => new { p, Local = ClinicCalendar.LocalDay(p.BirthDateUtc!.Value.UtcTicks, zone) })
            .Where(x => x.Local.Month == today.Month && x.Local.Day == today.Day)
            .ToArray();
        if (matches.Length == 0) return [];
        var ids = matches.Select(m => m.p.OwnerId).Distinct().ToArray();
        var owners = await db.Set<Owner>().AsNoTracking().Where(o => ids.Contains(o.Id))
            .Select(o => new { o.Id, o.FullName, o.PhonePrimary }).ToDictionaryAsync(o => o.Id, ct);
        return matches
            .Where(m => owners.ContainsKey(m.p.OwnerId))
            .Select(m => new BirthdayPetReadModel(m.p.Id, m.p.Name, m.p.Species.ToString(), m.p.OwnerId,
                owners[m.p.OwnerId].FullName, owners[m.p.OwnerId].PhonePrimary,
                Math.Max(0, today.Year - m.Local.Year)))
            .OrderBy(b => b.OwnerName, StringComparer.Ordinal).ThenBy(b => b.PetName, StringComparer.Ordinal).ToArray();
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task<FinanceTotalsReadModel> TotalsAsync(FinanceDayRange range, CancellationToken ct)
    {
        var days = await FinanceAnalytics.DaysAsync(db, range.FromDay, range.ToDay, zones.GetClinicTimeZone(), ct);
        var (from, to) = UtcBounds(range);
        var visits = await db.Set<Visit>().AsNoTracking().CountAsync(v => v.TenantId == Tenant
            && v.State != VisitState.Cancelled && v.StartedUtc >= from && v.StartedUtc < to, ct);
        return new FinanceTotalsReadModel(days.Values.Sum(d => d.Revenue), days.Values.Sum(d => d.Discount),
            days.Values.Sum(d => d.Refund), days.Values.Sum(d => d.Cogs), days.Values.Sum(d => d.Expense),
            days.Values.Sum(d => d.Invoices), days.Values.Sum(d => d.Paid), visits);
    }

    private async Task<Dictionary<string, (long Amount, int Count)>> CategoryTotalsAsync(FinanceDayRange range, CancellationToken ct)
    {
        var rows = await Expenses(range)
            .GroupBy(e => new { e.CategoryCode, Reversal = e.ReversesExpenseId != null })
            .Select(g => new { g.Key.CategoryCode, g.Key.Reversal, Amount = g.Sum(e => e.Amount.Minor), Count = g.Count() })
            .ToListAsync(ct);
        var totals = new Dictionary<string, (long Amount, int Count)>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var (amount, count) = totals.GetValueOrDefault(row.CategoryCode);
            totals[row.CategoryCode] = row.Reversal
                ? (amount - row.Amount, count)
                : (amount + row.Amount, count + row.Count);
        }
        return totals;
    }

    /// <summary>Posted or reversed expenses (never drafts) incurred inside the window.</summary>
    private IQueryable<Expense> Expenses(FinanceDayRange range)
    {
        var (from, to) = UtcBounds(range);
        return db.Set<Expense>().AsNoTracking().Where(e => e.TenantId == Tenant && e.State != ExpenseState.Draft
            && e.IncurredUtc >= from && e.IncurredUtc < to);
    }

    private async Task<List<DateOnly>> VisitDaysAsync(FinanceDayRange range, CancellationToken ct)
    {
        var (from, to) = UtcBounds(range);
        var zone = zones.GetClinicTimeZone();
        var started = await db.Set<Visit>().AsNoTracking().Where(v => v.TenantId == Tenant
                && v.State != VisitState.Cancelled && v.StartedUtc >= from && v.StartedUtc < to)
            .Select(v => v.StartedUtc).ToListAsync(ct);
        return started.Select(s => ClinicCalendar.LocalDay(s.UtcTicks, zone)).ToList();
    }

    private (DateTimeOffset From, DateTimeOffset To) UtcBounds(FinanceDayRange range)
    {
        var zone = zones.GetClinicTimeZone();
        return (new DateTimeOffset(ClinicCalendar.ToUtcTicks(Parse(range.FromDay), 0, zone), TimeSpan.Zero),
            new DateTimeOffset(ClinicCalendar.ToUtcTicks(Parse(range.ToDay).AddDays(1), 0, zone), TimeSpan.Zero));
    }

    /// <summary>The window of equal length that ends the day before <paramref name="range"/> starts.</summary>
    internal static FinanceDayRange Previous(FinanceDayRange range)
    {
        var from = Parse(range.FromDay);
        var length = Parse(range.ToDay).DayNumber - from.DayNumber + 1;
        return new FinanceDayRange(Key(from.AddDays(-length)), Key(from.AddDays(-1)));
    }

    /// <summary>Gap-free bucket starts: every day, every 7 days from the window start, or every Jalali month.</summary>
    internal static List<DateOnly> Buckets(FinanceDayRange range, string bucket)
    {
        var from = Parse(range.FromDay);
        var to = Parse(range.ToDay);
        var starts = new List<DateOnly>();
        switch (bucket)
        {
            case "week":
                for (var d = from; d <= to; d = d.AddDays(7)) starts.Add(d);
                break;
            case "month":
                var dt = from.ToDateTime(TimeOnly.MinValue);
                var first = DateOnly.FromDateTime(Jalali.ToDateTime(Jalali.GetYear(dt), Jalali.GetMonth(dt), 1, 0, 0, 0, 0));
                for (var d = first; d <= to; d = DateOnly.FromDateTime(Jalali.AddMonths(d.ToDateTime(TimeOnly.MinValue), 1)))
                    starts.Add(d < from ? from : d);
                break;
            default:
                for (var d = from; d <= to; d = d.AddDays(1)) starts.Add(d);
                break;
        }
        return starts;
    }

    private static int IndexOf(List<DateOnly> starts, DateOnly day)
    {
        if (starts.Count == 0 || day < starts[0]) return -1;
        var index = starts.BinarySearch(day);
        return index >= 0 ? index : ~index - 1;
    }

    private static DateOnly Parse(int day) => new(day / 10000, day / 100 % 100, day % 100);
    private static int Key(DateOnly day) => day.Year * 10000 + day.Month * 100 + day.Day;
}
