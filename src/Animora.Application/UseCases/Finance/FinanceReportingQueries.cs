using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Time;
using Animora.Application.UseCases.Finance.ReadModels;
using Animora.Domain.Finance;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Finance;

/// <summary>
/// The seven projection-only finance reporting queries (C09). Every handler reads only the
/// rebuildable facts through <see cref="IFinanceQueries"/> (A-RULE 034) and returns the lean
/// <c>*ReadModel</c> shapes already defined in <see cref="ReadModels"/>; the Client maps them to the
/// rich Contracts DTOs. The current local day is computed from the injected clock and the clinic
/// time zone, so day-bucketing stays out of SQL (CONVENTIONS §5).
/// </summary>
internal static class FinanceReportingTime
{
    public static int TodayLocalDay(IClock clock, IClinicTimeZoneProvider zones)
    {
        var d = ClinicCalendar.LocalDay(clock.UtcNow.UtcTicks, zones.GetClinicTimeZone());
        return (d.Year * 10000) + (d.Month * 100) + d.Day;
    }
}

// ── 1. Overview ──

[RequiresPermission(PermissionCodes.FinanceRead)]
public sealed record FinanceOverviewQuery(string Range) : IQuery<FinanceOverviewReadModel>;

public sealed class FinanceOverviewQueryHandler(
    IFinanceQueries queries, IClock clock, IClinicTimeZoneProvider zones)
    : IQueryHandler<FinanceOverviewQuery, FinanceOverviewReadModel>
{
    public async Task<Result<FinanceOverviewReadModel>> HandleAsync(
        FinanceOverviewQuery q, QueryContext context, CancellationToken ct)
    {
        var today = FinanceReportingTime.TodayLocalDay(clock, zones);
        var agg = await queries.GetAggregateAsync(q.Range, today, ct).ConfigureAwait(false);
        var channels = await queries.GetChannelsAsync(q.Range, today, ct).ConfigureAwait(false);
        var categories = await queries.GetExpenseCategoriesAsync(q.Range, today, ct).ConfigureAwait(false);
        var products = await queries.GetProductSalesAsync(q.Range, today, ct).ConfigureAwait(false);
        return Result<FinanceOverviewReadModel>.Ok(
            new FinanceOverviewReadModel(agg, channels, categories, products));
    }
}

// ── 2. Income ──

[RequiresPermission(PermissionCodes.FinanceRead)]
public sealed record FinanceIncomeQuery(string Range) : IQuery<FinanceIncomeReadModel>;

public sealed class FinanceIncomeQueryHandler(
    IFinanceQueries queries, IClock clock, IClinicTimeZoneProvider zones)
    : IQueryHandler<FinanceIncomeQuery, FinanceIncomeReadModel>
{
    public async Task<Result<FinanceIncomeReadModel>> HandleAsync(
        FinanceIncomeQuery q, QueryContext context, CancellationToken ct)
    {
        var today = FinanceReportingTime.TodayLocalDay(clock, zones);
        var agg = await queries.GetAggregateAsync(q.Range, today, ct).ConfigureAwait(false);
        var channels = await queries.GetChannelsAsync(q.Range, today, ct).ConfigureAwait(false);
        return Result<FinanceIncomeReadModel>.Ok(new FinanceIncomeReadModel(agg, channels));
    }
}

// ── 3. Expense ──

[RequiresPermission(PermissionCodes.FinanceRead)]
public sealed record FinanceExpenseQuery(string Range) : IQuery<FinanceExpenseReadModel>;

public sealed class FinanceExpenseQueryHandler(
    IFinanceQueries queries, IClock clock, IClinicTimeZoneProvider zones)
    : IQueryHandler<FinanceExpenseQuery, FinanceExpenseReadModel>
{
    public async Task<Result<FinanceExpenseReadModel>> HandleAsync(
        FinanceExpenseQuery q, QueryContext context, CancellationToken ct)
    {
        var today = FinanceReportingTime.TodayLocalDay(clock, zones);
        var agg = await queries.GetAggregateAsync(q.Range, today, ct).ConfigureAwait(false);
        var categories = await queries.GetExpenseCategoriesAsync(q.Range, today, ct).ConfigureAwait(false);
        var rows = await queries.GetExpenseRowsAsync(q.Range, today, ct).ConfigureAwait(false);
        var payments = await queries.GetPaymentMethodsAsync(q.Range, today, ct).ConfigureAwait(false);
        return Result<FinanceExpenseReadModel>.Ok(
            new FinanceExpenseReadModel(agg, categories, rows, payments));
    }
}

// ── 4. Profit ──

[RequiresPermission(PermissionCodes.FinanceRead)]
public sealed record FinanceProfitQuery(string Range) : IQuery<FinanceProfitReadModel>;

public sealed class FinanceProfitQueryHandler(
    IFinanceQueries queries, IClock clock, IClinicTimeZoneProvider zones)
    : IQueryHandler<FinanceProfitQuery, FinanceProfitReadModel>
{
    public async Task<Result<FinanceProfitReadModel>> HandleAsync(
        FinanceProfitQuery q, QueryContext context, CancellationToken ct)
    {
        var today = FinanceReportingTime.TodayLocalDay(clock, zones);
        var agg = await queries.GetAggregateAsync(q.Range, today, ct).ConfigureAwait(false);
        var products = await queries.GetProductSalesAsync(q.Range, today, ct).ConfigureAwait(false);
        return Result<FinanceProfitReadModel>.Ok(new FinanceProfitReadModel(agg, products));
    }
}

// ── 5. Range catalog (static, no DB) ──

[RequiresPermission(PermissionCodes.FinanceRead)]
public sealed record FinanceRangeCatalogQuery : IQuery<IReadOnlyList<FinanceRangeDefinition>>;

public sealed class FinanceRangeCatalogQueryHandler
    : IQueryHandler<FinanceRangeCatalogQuery, IReadOnlyList<FinanceRangeDefinition>>
{
    public Task<Result<IReadOnlyList<FinanceRangeDefinition>>> HandleAsync(
        FinanceRangeCatalogQuery q, QueryContext context, CancellationToken ct) =>
        Task.FromResult(Result<IReadOnlyList<FinanceRangeDefinition>>.Ok(FinanceRanges.All));
}

// ── 6. Expense category codes (static Domain catalog) ──

[RequiresPermission(PermissionCodes.FinanceRead)]
public sealed record FinanceExpenseCategoriesQuery : IQuery<IReadOnlyList<string>>;

public sealed class FinanceExpenseCategoriesQueryHandler
    : IQueryHandler<FinanceExpenseCategoriesQuery, IReadOnlyList<string>>
{
    public Task<Result<IReadOnlyList<string>>> HandleAsync(
        FinanceExpenseCategoriesQuery q, QueryContext context, CancellationToken ct) =>
        Task.FromResult(Result<IReadOnlyList<string>>.Ok(ExpenseCategories.All));
}

// ── 7. Payment methods (static presentation catalog) ──

[RequiresPermission(PermissionCodes.FinanceRead)]
public sealed record FinancePaymentMethodsQuery : IQuery<IReadOnlyList<PaymentMethod>>;

public sealed class FinancePaymentMethodsQueryHandler
    : IQueryHandler<FinancePaymentMethodsQuery, IReadOnlyList<PaymentMethod>>
{
    private static readonly IReadOnlyList<PaymentMethod> Methods =
        [PaymentMethod.Card, PaymentMethod.Cash, PaymentMethod.Transfer, PaymentMethod.Credit];

    public Task<Result<IReadOnlyList<PaymentMethod>>> HandleAsync(
        FinancePaymentMethodsQuery q, QueryContext context, CancellationToken ct) =>
        Task.FromResult(Result<IReadOnlyList<PaymentMethod>>.Ok(Methods));
}
