using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Contracts.Navigation;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Dashboard;

public sealed record DashboardActivity(string Kind, string TitleKey, string EntityId, long AmountMinor,
    int AmountSign, string ActorUserId);
public sealed record DashboardDay(int LocalDay, long RevenueMinor);
public sealed record DashboardWorkload(string DoctorId, int Total);
/// <summary>Full-day aggregates, independent of the bounded recent-activity preview.</summary>
public sealed record DashboardActivityTotals(long Payments, int Visits, int Vaccinations, int Cheques);
public sealed record DashboardSlice(IReadOnlyList<DashboardDay> RevenueDays,
    IReadOnlyList<DashboardActivity> Activity, IReadOnlyList<DashboardWorkload> Workloads,
    IReadOnlyDictionary<string, int> Species, int OpenInvoices, int LowStockCount, int QueueCount,
    DashboardActivityTotals? ActivityTotals = null);

/// <summary>The local day and its half-open UTC range, resolved once using ClinicCalendar.</summary>
public sealed record DashboardDayWindow(int LocalDay, DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
    public static DashboardDayWindow For(DateOnly day, TimeZoneInfo zone)
    {
        var (start, end) = Animora.Application.Time.ClinicCalendar.DayRangeUtc(day, zone);
        return new(day.Year * 10000 + day.Month * 100 + day.Day,
            new DateTimeOffset(start, TimeSpan.Zero), new DateTimeOffset(end, TimeSpan.Zero));
    }
}

public interface IDashboardQueries
{
    Task<string> VersionAsync(string tenantId, CancellationToken ct);
    Task<DashboardSlice> GetSliceAsync(string tenantId, DashboardDayWindow window, string userId, UserRole role, CancellationToken ct);
}

public sealed record DashboardSnapshotQuery(UserRole Role, DashboardDayWindow? Window = null) : IQuery<DashboardSlice>;

/// <summary>Sequentially assembles role-aware slices over one scoped local connection.</summary>
public sealed class DashboardComposer(IDashboardQueries queries, Animora.Domain.Time.IClock clock,
    IClinicTimeZoneProvider zones) : IQueryHandler<DashboardSnapshotQuery, DashboardSlice>
{
    public async Task<Result<DashboardSlice>> HandleAsync(DashboardSnapshotQuery query, QueryContext context, CancellationToken ct)
    {
        var window = query.Window;
        if (window is null)
        {
            var zone = zones.GetClinicTimeZone();
            var date = Animora.Application.Time.ClinicCalendar.LocalDay(clock.UtcNow.UtcTicks, zone);
            window = DashboardDayWindow.For(date, zone);
        }
        return Result<DashboardSlice>.Ok(await queries.GetSliceAsync(context.TenantId, window, context.UserId, query.Role, ct));
    }
}

public static class DashboardRegistration
{
    public static Animora.Application.Dispatch.ApplicationRegistrationBuilder AddDashboardUseCases(
        this Animora.Application.Dispatch.ApplicationRegistrationBuilder builder) => builder
        .AddQuery<DashboardSnapshotQuery, DashboardSlice, DashboardComposer>();
}
