using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Finance.ReadModels;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Finance;

/// <summary>
/// Dispatcher wrappers over <see cref="IStatisticsQueries"/> (1:1 with the port). Each handler resolves the
/// requested <see cref="StatisticsWindow"/> against the clinic clock, then delegates to the store.
/// </summary>
internal static class StatisticsQuerySupport
{
    public static async Task<Result<T>> Run<T>(StatisticsWindow window, IClock clock, IClinicTimeZoneProvider zones,
        Func<ResolvedStatisticsWindow, Task<T>> read)
    {
        var resolved = StatisticsWindowResolver.Resolve(window, clock, zones);
        return resolved.IsFailure ? Result<T>.Fail(resolved.Error, resolved.Detail) : Result<T>.Ok(await read(resolved.Value!));
    }
}

[RequiresPermission(PermissionCodes.ReportsRead)]
public sealed record StatisticsOverviewQuery(StatisticsWindow Window) : IQuery<StatisticsOverviewReadModel>;

public sealed class StatisticsOverviewQueryHandler(IStatisticsQueries queries, IClock clock, IClinicTimeZoneProvider zones)
    : IQueryHandler<StatisticsOverviewQuery, StatisticsOverviewReadModel>
{
    public Task<Result<StatisticsOverviewReadModel>> HandleAsync(StatisticsOverviewQuery q, QueryContext context, CancellationToken ct) =>
        StatisticsQuerySupport.Run(q.Window, clock, zones, w => queries.GetOverviewAsync(w.Range, w.TodayLocalDay, ct));
}

[RequiresPermission(PermissionCodes.ReportsRead)]
public sealed record StatisticsRevenueSeriesQuery(StatisticsWindow Window) : IQuery<IReadOnlyList<StatisticsSeriesPoint>>;

public sealed class StatisticsRevenueSeriesQueryHandler(IStatisticsQueries queries, IClock clock, IClinicTimeZoneProvider zones)
    : IQueryHandler<StatisticsRevenueSeriesQuery, IReadOnlyList<StatisticsSeriesPoint>>
{
    public Task<Result<IReadOnlyList<StatisticsSeriesPoint>>> HandleAsync(StatisticsRevenueSeriesQuery q, QueryContext context, CancellationToken ct) =>
        StatisticsQuerySupport.Run(q.Window, clock, zones, w => queries.GetRevenueSeriesAsync(w.Range, w.Bucket, ct));
}

[RequiresPermission(PermissionCodes.ReportsRead)]
public sealed record StatisticsRevenueBySourceQuery(StatisticsWindow Window) : IQuery<IReadOnlyList<RevenueSourceReadModel>>;

public sealed class StatisticsRevenueBySourceQueryHandler(IStatisticsQueries queries, IClock clock, IClinicTimeZoneProvider zones)
    : IQueryHandler<StatisticsRevenueBySourceQuery, IReadOnlyList<RevenueSourceReadModel>>
{
    public Task<Result<IReadOnlyList<RevenueSourceReadModel>>> HandleAsync(StatisticsRevenueBySourceQuery q, QueryContext context, CancellationToken ct) =>
        StatisticsQuerySupport.Run(q.Window, clock, zones, w => queries.GetRevenueBySourceAsync(w.Range, ct));
}

[RequiresPermission(PermissionCodes.FinanceRead)]
public sealed record ExpenseListQuery(StatisticsWindow Window, string? CategoryCode = null, int Page = 1, int PageSize = 50)
    : IQuery<PagedResult<ExpenseRowReadModel>>;

public sealed class ExpenseListQueryHandler(IStatisticsQueries queries, IClock clock, IClinicTimeZoneProvider zones)
    : IQueryHandler<ExpenseListQuery, PagedResult<ExpenseRowReadModel>>
{
    public Task<Result<PagedResult<ExpenseRowReadModel>>> HandleAsync(ExpenseListQuery q, QueryContext context, CancellationToken ct) =>
        StatisticsQuerySupport.Run(q.Window, clock, zones,
            w => queries.GetExpensesAsync(w.Range, q.CategoryCode, q.Page, q.PageSize, ct));
}

[RequiresPermission(PermissionCodes.FinanceRead)]
public sealed record ExpenseStatsQuery(StatisticsWindow Window) : IQuery<ExpenseStatsReadModel>;

public sealed class ExpenseStatsQueryHandler(IStatisticsQueries queries, IClock clock, IClinicTimeZoneProvider zones)
    : IQueryHandler<ExpenseStatsQuery, ExpenseStatsReadModel>
{
    public Task<Result<ExpenseStatsReadModel>> HandleAsync(ExpenseStatsQuery q, QueryContext context, CancellationToken ct) =>
        StatisticsQuerySupport.Run(q.Window, clock, zones, w => queries.GetExpenseStatsAsync(w.Range, w.Bucket, ct));
}

[RequiresPermission(PermissionCodes.ReportsRead)]
public sealed record TopProductsQuery(StatisticsWindow Window, int Take = 10) : IQuery<IReadOnlyList<TopProductReadModel>>;

public sealed class TopProductsQueryHandler(IStatisticsQueries queries, IClock clock, IClinicTimeZoneProvider zones)
    : IQueryHandler<TopProductsQuery, IReadOnlyList<TopProductReadModel>>
{
    public Task<Result<IReadOnlyList<TopProductReadModel>>> HandleAsync(TopProductsQuery q, QueryContext context, CancellationToken ct) =>
        StatisticsQuerySupport.Run(q.Window, clock, zones, w => queries.GetTopProductsAsync(w.Range, q.Take, ct));
}

[RequiresPermission(PermissionCodes.ReportsRead)]
public sealed record TopClientsQuery(int Take = 10) : IQuery<IReadOnlyList<TopClientReadModel>>;

public sealed class TopClientsQueryHandler(IStatisticsQueries queries) : IQueryHandler<TopClientsQuery, IReadOnlyList<TopClientReadModel>>
{
    public async Task<Result<IReadOnlyList<TopClientReadModel>>> HandleAsync(TopClientsQuery q, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<TopClientReadModel>>.Ok(await queries.GetTopClientsAsync(q.Take, ct));
}

[RequiresPermission(PermissionCodes.ReportsRead)]
public sealed record SpeciesDistributionQuery : IQuery<IReadOnlyList<SpeciesSliceReadModel>>;

public sealed class SpeciesDistributionQueryHandler(IStatisticsQueries queries)
    : IQueryHandler<SpeciesDistributionQuery, IReadOnlyList<SpeciesSliceReadModel>>
{
    public async Task<Result<IReadOnlyList<SpeciesSliceReadModel>>> HandleAsync(SpeciesDistributionQuery q, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<SpeciesSliceReadModel>>.Ok(await queries.GetSpeciesDistributionAsync(ct));
}

[RequiresPermission(PermissionCodes.ReportsRead)]
public sealed record ClientStatsQuery(StatisticsWindow Window) : IQuery<ClientStatsReadModel>;

public sealed class ClientStatsQueryHandler(IStatisticsQueries queries, IClock clock, IClinicTimeZoneProvider zones)
    : IQueryHandler<ClientStatsQuery, ClientStatsReadModel>
{
    public Task<Result<ClientStatsReadModel>> HandleAsync(ClientStatsQuery q, QueryContext context, CancellationToken ct) =>
        StatisticsQuerySupport.Run(q.Window, clock, zones, w => queries.GetClientStatsAsync(w.Range, ct));
}
