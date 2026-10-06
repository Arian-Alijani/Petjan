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
/// «آمار» read projections. Finance totals come from the pre-aggregated <see cref="DailyFinanceFact"/> rows
/// (index <c>IX_DailyFinanceFacts_TenantId_LocalDay</c>), products from <see cref="ProductSalesFact"/>, clients
/// from <see cref="OwnerFinancialSnapshot"/>. Day windows are converted to UTC bounds once with
/// <see cref="ClinicCalendar"/> and bucketed in memory — never with SQLite date functions.
/// </summary>
public sealed class StatisticsQueries(AnimoraLocalDbContext db, IClinicTimeZoneProvider zones,
    IOptions<LocalStoreOptions> options) : IStatisticsQueries
{
    private const int MaxTake = 50;
    private static readonly PersianCalendar Jalali = new();
    private static readonly InvoiceState[] RevenueStates =
        [InvoiceState.Posted, InvoiceState.PartiallyPaid, InvoiceState.Paid, InvoiceState.Refunded];

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
        var facts = await db.Set<DailyFinanceFact>().AsNoTracking()
            .Where(f => f.TenantId == Tenant && f.LocalDay >= range.FromDay && f.LocalDay <= range.ToDay)
            .Select(f => new { f.LocalDay, Revenue = f.RevenueMinor - f.DiscountMinor - f.RefundMinor,
                f.ExpenseMinor, f.CogsMinor, f.InvoiceCount })
            .ToListAsync(ct);
        var visits = await VisitDaysAsync(range, ct);
        var buckets = Buckets(range, bucket);
        var revenue = new long[buckets.Count];
        var expense = new long[buckets.Count];
        var cogs = new long[buckets.Count];
        var invoices = new int[buckets.Count];
        var visitCounts = new int[buckets.Count];
        foreach (var f in facts)
        {
            var i = IndexOf(buckets, Parse(f.LocalDay));
            if (i < 0) continue;
            revenue[i] += f.Revenue;
            expense[i] += f.ExpenseMinor;
            cogs[i] += f.CogsMinor;
            invoices[i] += f.InvoiceCount;
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
        var lines = await db.Set<Invoice>().AsNoTracking()
            .Where(i => i.TenantId == Tenant && RevenueStates.Contains(i.State)
                && i.PostedUtc != null && i.PostedUtc >= from && i.PostedUtc < to)
            .SelectMany(i => i.Lines, (i, l) => new { Return = i.Kind == InvoiceKind.SalesReturn, l.Kind, l.Source,
                l.Quantity.Value, Unit = l.UnitPrice.Minor, l.DiscountPercent })
            .ToListAsync(ct);
        var totals = new Dictionary<string, (long Amount, int Count)>(StringComparer.Ordinal)
        {
            ["visit"] = (0, 0), ["product"] = (0, 0), ["boarding"] = (0, 0), ["other"] = (0, 0),
        };
        foreach (var line in lines)
        {
            var source = line.Kind switch
            {
                InvoiceLineKind.Product => "product",
                InvoiceLineKind.Boarding => "boarding",
                InvoiceLineKind.Service when line.Source == InvoiceLineSource.Stay => "boarding",
                InvoiceLineKind.Service => "visit",
                _ => "other",
            };
            var gross = line.Value * line.Unit;
            var net = (long)decimal.Round(gross - gross * line.DiscountPercent / 100m, 0, MidpointRounding.AwayFromZero);
            var (amount, count) = totals[source];
            totals[source] = (amount + (line.Return ? -net : net), count + 1);
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
        var facts = db.Set<ProductSalesFact>().AsNoTracking()
            .Where(f => f.TenantId == Tenant && f.LocalDay >= range.FromDay && f.LocalDay <= range.ToDay);
        var top = await facts.GroupBy(f => f.ProductId)
            .Select(g => new { ProductId = g.Key, Revenue = g.Sum(f => f.RevenueMinor), Cogs = g.Sum(f => f.CogsMinor) })
            .Where(g => g.Revenue > 0)
            .OrderByDescending(g => g.Revenue).ThenBy(g => g.ProductId).Take(take).ToListAsync(ct);
        if (top.Count == 0) return [];
        var ids = top.Select(t => t.ProductId).ToArray();
        // Decimal quantities are stored as exact TEXT, which SQLite cannot SUM: add the bounded rows here.
        var quantities = (await facts.Where(f => ids.Contains(f.ProductId))
                .Select(f => new { f.ProductId, f.QuantitySold }).ToListAsync(ct))
            .GroupBy(f => f.ProductId).ToDictionary(g => g.Key, g => g.Sum(f => f.QuantitySold), StringComparer.Ordinal);
        var names = await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Name }).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        return top.Select(t => new TopProductReadModel(t.ProductId, names.GetValueOrDefault(t.ProductId) ?? t.ProductId,
            quantities.GetValueOrDefault(t.ProductId), t.Revenue, t.Cogs)).ToArray();
    }

    public async Task<IReadOnlyList<TopClientReadModel>> GetTopClientsAsync(int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, MaxTake);
        return await (from s in db.Set<OwnerFinancialSnapshot>().AsNoTracking()
            join o in db.Set<Owner>().AsNoTracking() on s.OwnerId equals o.Id
            where s.TenantId == Tenant && s.TotalSpendMinor > 0
            orderby s.TotalSpendMinor descending, s.OwnerId
            select new TopClientReadModel(s.OwnerId, o.FullName, o.PhonePrimary, s.TotalSpendMinor,
                s.InvoiceCount, s.LastInvoiceUtc)).Take(take).ToListAsync(ct);
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
        var invoices = db.Set<Invoice>().AsNoTracking().Where(i => i.TenantId == Tenant && i.OwnerId != null
            && RevenueStates.Contains(i.State) && i.PostedUtc != null);
        var active = invoices.Where(i => i.PostedUtc >= from && i.PostedUtc < to).Select(i => i.OwnerId).Distinct();
        var activeCount = await active.CountAsync(ct);
        var returning = await active.CountAsync(id => invoices.Any(p => p.OwnerId == id && p.PostedUtc < from), ct);
        return new ClientStatsReadModel(registered, activeCount, returning, total);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task<FinanceTotalsReadModel> TotalsAsync(FinanceDayRange range, CancellationToken ct)
    {
        var sums = await db.Set<DailyFinanceFact>().AsNoTracking()
            .Where(f => f.TenantId == Tenant && f.LocalDay >= range.FromDay && f.LocalDay <= range.ToDay)
            .GroupBy(f => 1).Select(g => new
            {
                Revenue = g.Sum(f => f.RevenueMinor), Discount = g.Sum(f => f.DiscountMinor),
                Refund = g.Sum(f => f.RefundMinor), Cogs = g.Sum(f => f.CogsMinor),
                Expense = g.Sum(f => f.ExpenseMinor), Invoices = g.Sum(f => f.InvoiceCount),
                Paid = g.Sum(f => f.PaidInvoiceCount),
            }).SingleOrDefaultAsync(ct);
        var (from, to) = UtcBounds(range);
        var visits = await db.Set<Visit>().AsNoTracking().CountAsync(v => v.TenantId == Tenant
            && v.State != VisitState.Cancelled && v.StartedUtc >= from && v.StartedUtc < to, ct);
        return sums is null
            ? FinanceTotalsReadModel.Empty with { VisitCount = visits }
            : new FinanceTotalsReadModel(sums.Revenue, sums.Discount, sums.Refund, sums.Cogs, sums.Expense,
                sums.Invoices, sums.Paid, visits);
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
