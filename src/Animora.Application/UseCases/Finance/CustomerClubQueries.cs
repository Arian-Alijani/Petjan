using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Time;
using Animora.Application.UseCases.Finance.ReadModels;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Finance;

// «باشگاه مشتریان» reads. They live beside the statistics queries because they share the same read-side port
// (IStatisticsQueries) and the same client-leaderboard read model; "top spenders" reuses TopClientsQuery as-is.

/// <summary>Clients ranked by visit frequency (invoice count), ties broken by lifetime spend.</summary>
[RequiresPermission(PermissionCodes.ReportsRead)]
public sealed record TopVisitorsQuery(int Take = 10) : IQuery<IReadOnlyList<TopClientReadModel>>;

public sealed class TopVisitorsQueryHandler(IStatisticsQueries queries) : IQueryHandler<TopVisitorsQuery, IReadOnlyList<TopClientReadModel>>
{
    public async Task<Result<IReadOnlyList<TopClientReadModel>>> HandleAsync(TopVisitorsQuery q, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<TopClientReadModel>>.Ok(await queries.GetTopVisitorsAsync(q.Take, ct));
}

/// <summary>Active patients whose birthday is today (clinic-local), with their owner's contact.</summary>
[RequiresPermission(PermissionCodes.PatientsRead)]
public sealed record TodaysBirthdaysQuery : IQuery<IReadOnlyList<BirthdayPetReadModel>>;

public sealed class TodaysBirthdaysQueryHandler(IStatisticsQueries queries, IClock clock, IClinicTimeZoneProvider zones)
    : IQueryHandler<TodaysBirthdaysQuery, IReadOnlyList<BirthdayPetReadModel>>
{
    public async Task<Result<IReadOnlyList<BirthdayPetReadModel>>> HandleAsync(TodaysBirthdaysQuery q, QueryContext context, CancellationToken ct)
    {
        var today = ClinicCalendar.LocalDay(clock.UtcNow.UtcTicks, zones.GetClinicTimeZone());
        return Result<IReadOnlyList<BirthdayPetReadModel>>.Ok(
            await queries.GetTodaysBirthdaysAsync(StatisticsWindowResolver.Key(today), ct));
    }
}
