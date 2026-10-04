using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Contracts.History;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.History;

public sealed record ActivityRow(string Id, DateTimeOffset OccurredUtc, string Kind, string TitleKey,
    string DetailArgsJson, string EntityType, string EntityId, long AmountMinor, int AmountSign,
    string Status, string ActorUserId, string ActorName, string DetailText = "", int LocalDay = 0);

/// <param name="DayNets">Net (income − expense) per clinic-local day (<c>yyyymmdd</c>) for every day present on the page,
/// computed over the whole filtered set so a day split across pages reports the same full-day figure on each.</param>
public sealed record ActivityPage(IReadOnlyList<ActivityRow> Items, int TotalCount, long IncomeTotal, long ExpenseTotal,
    IReadOnlyDictionary<int, long>? DayNets = null);

public sealed record ActivityFeedQuery(HistoryFilter Filter) : IQuery<ActivityPage>;
public sealed record ActivityExportQuery(HistoryFilter Filter) : IQuery<IReadOnlyList<ActivityRow>>;

public sealed class ActivityFeedQueryHandler(IActivityFeedQueries queries) : IQueryHandler<ActivityFeedQuery, ActivityPage>
{
    public async Task<Result<ActivityPage>> HandleAsync(ActivityFeedQuery query, QueryContext context, CancellationToken ct) =>
        Result<ActivityPage>.Ok(await queries.PageAsync(context.TenantId, query.Filter, ct));
}

public sealed class ActivityExportQueryHandler(IActivityFeedQueries queries) : IQueryHandler<ActivityExportQuery, IReadOnlyList<ActivityRow>>
{
    public async Task<Result<IReadOnlyList<ActivityRow>>> HandleAsync(ActivityExportQuery query, QueryContext context, CancellationToken ct)
    {
        var rows = await queries.ExportAsync(context.TenantId, query.Filter, ct);
        return rows.Count > 50_000
            ? Result<IReadOnlyList<ActivityRow>>.Fail(ErrorCodes.ExportTooLarge)
            : Result<IReadOnlyList<ActivityRow>>.Ok(rows);
    }
}

public interface IActivityFeedQueries
{
    Task<ActivityPage> PageAsync(string tenantId, HistoryFilter filter, CancellationToken ct);
    Task<IReadOnlyList<ActivityRow>> ExportAsync(string tenantId, HistoryFilter filter, CancellationToken ct);
}

public static class HistoryRegistration
{
    public static Animora.Application.Dispatch.ApplicationRegistrationBuilder AddHistoryUseCases(
        this Animora.Application.Dispatch.ApplicationRegistrationBuilder builder) => builder
        .AddQuery<ActivityFeedQuery, ActivityPage, ActivityFeedQueryHandler>()
        .AddQuery<ActivityExportQuery, IReadOnlyList<ActivityRow>, ActivityExportQueryHandler>();
}
