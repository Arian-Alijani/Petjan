using Animora.Application.Ports;
using Animora.Application.Time;
using Animora.Application.UseCases.Finance.ReadModels;
using Animora.Domain.Finance;
using Animora.Domain.Inventory;
using Animora.Domain.Sales;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>
/// Projection-only finance reads (C09). The KPI aggregates come from the rebuildable
/// <see cref="DailyFinanceFact"/>/<see cref="ProductSalesFact"/> cache, never a live scan of
/// <c>Invoices</c>/<c>JournalLines</c> (A-RULE 034). Only the small expense/payment-method lists read
/// the live <c>Expenses</c>/<c>Payments</c> rows. Tenant filtering is automatic
/// (<c>ModelConventions</c>); day-bucketing happened at projection time, never in SQL (CONVENTIONS §5).
/// </summary>
public sealed class FinanceQueries(AnimoraLocalDbContext db, IClinicTimeZoneProvider zones) : IFinanceQueries
{
    private static readonly string[] ChannelOrder = ["clinic", "pharmacy", "petshop", "lab"];

    public async Task<FinanceAggregateReadModel> GetAggregateAsync(
        string rangeId, int todayLocalDay, CancellationToken ct)
    {
        var range = Resolve(rangeId, todayLocalDay);
        var span = range.ToDay - range.FromDay; // inclusive window length − 1
        var previousTo = AddDays(range.FromDay, -1);
        var previousFrom = AddDays(previousTo, -span);

        var days = await DailyFactsAsync(range.FromDay, range.ToDay, ct);
        var previous = await TotalsAsync(previousFrom, previousTo, ct);
        var current = await TotalsAsync(range.FromDay, range.ToDay, ct);

        var definition = DefinitionOf(rangeId);
        return new FinanceAggregateReadModel(
            definition.Id, definition.Label, definition.Days, definition.Bucket, range, current, previous, days);
    }

    public async Task<IReadOnlyList<FinanceChannelReadModel>> GetChannelsAsync(
        string rangeId, int todayLocalDay, CancellationToken ct)
    {
        var range = Resolve(rangeId, todayLocalDay);
        var products = await ProductSalesAsync(range, ct);
        if (products.Count == 0) return [];

        var totals = new Dictionary<string, long>(StringComparer.Ordinal);
        for (var i = 0; i < products.Count; i++)
        {
            var channel = ChannelOf(products[i]);
            totals[channel] = totals.TryGetValue(channel, out var t) ? t + products[i].RevenueMinor : products[i].RevenueMinor;
        }

        var ordered = new List<FinanceChannelReadModel>(totals.Count);
        for (var i = 0; i < ChannelOrder.Length; i++)
        {
            if (totals.TryGetValue(ChannelOrder[i], out var value))
                ordered.Add(new FinanceChannelReadModel(ChannelOrder[i], value));
        }
        return ordered;
    }

    public async Task<IReadOnlyList<FinanceCategoryReadModel>> GetExpenseCategoriesAsync(
        string rangeId, int todayLocalDay, CancellationToken ct)
    {
        var range = Resolve(rangeId, todayLocalDay);
        var (fromUtc, toUtc) = RangeUtc(range);
        var rows = await db.Set<Expense>().AsNoTracking()
            .Where(e => e.State != ExpenseState.Draft && e.IncurredUtc >= fromUtc && e.IncurredUtc < toUtc)
            .GroupBy(e => e.CategoryCode)
            .Select(g => new { Category = g.Key, Amount = g.Sum(e => e.Amount.Minor) })
            .ToListAsync(ct);
        return rows.Select(r => new FinanceCategoryReadModel(r.Category, r.Amount)).ToArray();
    }

    public async Task<IReadOnlyList<ProductSalesRow>> GetProductSalesAsync(
        string rangeId, int todayLocalDay, CancellationToken ct) =>
        await ProductSalesAsync(Resolve(rangeId, todayLocalDay), ct);

    public async Task<IReadOnlyList<ExpenseRowReadModel>> GetExpenseRowsAsync(
        string rangeId, int todayLocalDay, CancellationToken ct)
    {
        var range = Resolve(rangeId, todayLocalDay);
        var (fromUtc, toUtc) = RangeUtc(range);
        var rows = await db.Set<Expense>().AsNoTracking()
            .Where(e => e.State != ExpenseState.Draft && e.IncurredUtc >= fromUtc && e.IncurredUtc < toUtc)
            .OrderByDescending(e => e.IncurredUtc).ThenByDescending(e => e.Id)
            .Select(e => new { e.Id, e.Title, e.CategoryCode, e.SettledMethod, e.Amount, e.IncurredUtc, e.CreatedByUserId })
            .ToListAsync(ct);
        var list = new List<ExpenseRowReadModel>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            list.Add(new ExpenseRowReadModel(r.Id, r.Title, r.CategoryCode, r.SettledMethod,
                r.Amount.Minor, r.IncurredUtc, r.CreatedByUserId));
        }
        return list;
    }

    public async Task<IReadOnlyList<PaymentMethodTotal>> GetPaymentMethodsAsync(
        string rangeId, int todayLocalDay, CancellationToken ct)
    {
        var range = Resolve(rangeId, todayLocalDay);
        var (fromUtc, toUtc) = RangeUtc(range);
        var rows = await db.Set<Payment>().AsNoTracking()
            .Where(p => p.Amount.Minor > 0 && p.Method != PaymentMethod.Credit
                && p.ReceivedUtc >= fromUtc && p.ReceivedUtc < toUtc)
            .GroupBy(p => p.Method)
            .Select(g => new { Method = g.Key, Amount = g.Sum(p => p.Amount.Minor) })
            .ToListAsync(ct);
        return rows.Select(r => new PaymentMethodTotal(r.Method, r.Amount)).ToArray();
    }

    public async Task<int> GetMaxProjectedDayAsync(CancellationToken ct)
    {
        var max = await db.Set<DailyFinanceFact>().AsNoTracking()
            .Select(f => (int?)f.LocalDay).MaxAsync(ct);
        return max ?? 0;
    }

    // ── fact reads ───────────────────────────────────────────────────────────

    private Task<List<DailyFinanceRow>> DailyFactsAsync(int fromDay, int toDay, CancellationToken ct) =>
        db.Set<DailyFinanceFact>().AsNoTracking()
            .Where(f => f.LocalDay >= fromDay && f.LocalDay <= toDay)
            .OrderBy(f => f.LocalDay)
            .Select(f => new DailyFinanceRow(f.LocalDay, f.RevenueMinor, f.CogsMinor, f.ExpenseMinor, f.RefundMinor))
            .ToListAsync(ct);

    private async Task<FinanceTotals> TotalsAsync(int fromDay, int toDay, CancellationToken ct)
    {
        var rows = await db.Set<DailyFinanceFact>().AsNoTracking()
            .Where(f => f.LocalDay >= fromDay && f.LocalDay <= toDay)
            .Select(f => new
            {
                f.RevenueMinor, f.CogsMinor, f.ExpenseMinor, f.DiscountMinor,
                f.InsuranceMinor, f.RefundMinor, f.InvoiceCount, f.PaidInvoiceCount,
            })
            .ToListAsync(ct);
        long revenue = 0, cogs = 0, expense = 0, discount = 0, insurance = 0, refund = 0;
        var invoices = 0;
        var paid = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            revenue += rows[i].RevenueMinor;
            cogs += rows[i].CogsMinor;
            expense += rows[i].ExpenseMinor;
            discount += rows[i].DiscountMinor;
            insurance += rows[i].InsuranceMinor;
            refund += rows[i].RefundMinor;
            invoices += rows[i].InvoiceCount;
            paid += rows[i].PaidInvoiceCount;
        }
        return new FinanceTotals(revenue, cogs, expense, discount, insurance, refund, invoices, paid);
    }

    private async Task<List<ProductSalesRow>> ProductSalesAsync(FinanceDayRange range, CancellationToken ct)
    {
        var facts = await db.Set<ProductSalesFact>().AsNoTracking()
            .Where(f => f.LocalDay >= range.FromDay && f.LocalDay <= range.ToDay)
            .ToListAsync(ct);
        if (facts.Count == 0) return [];

        var productIds = facts.Select(f => f.ProductId).Distinct().ToArray();
        var meta = await db.Set<Product>().AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new ProductMeta(p.Id, p.Name, p.Kind, p.CategoryId))
            .ToListAsync(ct);
        var byId = new Dictionary<string, ProductMeta>(meta.Count, StringComparer.Ordinal);
        for (var i = 0; i < meta.Count; i++) byId[meta[i].Id] = meta[i];

        var list = new List<ProductSalesRow>(facts.Count);
        for (var i = 0; i < facts.Count; i++)
        {
            var f = facts[i];
            var m = byId.TryGetValue(f.ProductId, out var found) ? found : default;
            list.Add(new ProductSalesRow(f.ProductId, f.VariantId, m.CategoryId ?? string.Empty,
                m.Name ?? f.ProductId, (m.Kind ?? ProductKind.PetShop).ToString(),
                f.QuantitySold, f.RevenueMinor, f.CogsMinor));
        }
        return list;
    }

    private readonly record struct ProductMeta(string Id, string? Name, ProductKind? Kind, string? CategoryId);

    /// <summary>
    /// Maps a sold product/service to a revenue channel (mirrors the reference <c>REVENUE_CHANNELS</c>):
    /// <c>LAB</c> category → lab; services → clinic; medicines/vaccines → pharmacy; everything else → pet-shop.
    /// </summary>
    private static string ChannelOf(ProductSalesRow row)
    {
        if (string.Equals(row.CategoryId, "LAB", StringComparison.Ordinal)) return "lab";
        return row.Kind switch
        {
            nameof(ProductKind.Service) => "clinic",
            nameof(ProductKind.Medicine) or nameof(ProductKind.Vaccine) => "pharmacy",
            _ => "petshop",
        };
    }

    // ── day/UTC helpers ────────────────────────────────────────────────────────

    /// <summary>The half-open UTC window covering the whole local day range.</summary>
    private (DateTimeOffset From, DateTimeOffset To) RangeUtc(FinanceDayRange range)
    {
        var tz = zones.GetClinicTimeZone();
        var startTicks = ClinicCalendar.ToUtcTicks(ToDate(range.FromDay), 0, tz);
        var endTicks = ClinicCalendar.ToUtcTicks(ToDate(range.ToDay).AddDays(1), 0, tz);
        return (new DateTimeOffset(startTicks, TimeSpan.Zero), new DateTimeOffset(endTicks, TimeSpan.Zero));
    }

    private static FinanceDayRange Resolve(string rangeId, int todayLocalDay)
    {
        var resolved = FinanceRanges.Resolve(rangeId, todayLocalDay);
        return resolved.IsSuccess
            ? resolved.Value!
            : FinanceRanges.Resolve("30d", todayLocalDay).Value!;
    }

    private static FinanceRangeDefinition DefinitionOf(string rangeId)
    {
        var all = FinanceRanges.All;
        for (var i = 0; i < all.Count; i++)
            if (string.Equals(all[i].Id, rangeId, StringComparison.Ordinal)) return all[i];
        return all[2]; // 30d default
    }

    private static int AddDays(int day, int delta) => ToDay(ToDate(day).AddDays(delta));

    private static DateOnly ToDate(int day) =>
        DateOnly.ParseExact(day.ToString("D8", System.Globalization.CultureInfo.InvariantCulture),
            "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);

    private static int ToDay(DateOnly day) => day.Year * 10000 + day.Month * 100 + day.Day;
}
