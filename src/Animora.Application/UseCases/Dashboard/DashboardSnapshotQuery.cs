using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Contracts.Navigation;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Dashboard;

public sealed record DashboardActivity(string Kind, string TitleKey, string EntityId, long AmountMinor,
    int AmountSign, string ActorUserId);
public sealed record DashboardDay(int LocalDay, long RevenueMinor);
public sealed record DashboardWorkload(string DoctorId, int Total);
public sealed record DashboardSlice(IReadOnlyList<DashboardDay> RevenueDays,
    IReadOnlyList<DashboardActivity> Activity, IReadOnlyList<DashboardWorkload> Workloads,
    IReadOnlyDictionary<string, int> Species, int OpenInvoices, int LowStockCount, int QueueCount);

public interface IDashboardQueries
{
    Task<string> VersionAsync(string tenantId, CancellationToken ct);
    Task<DashboardSlice> GetSliceAsync(string tenantId, int today, string userId, UserRole role, CancellationToken ct);
}

public sealed record DashboardSnapshotQuery(UserRole Role) : IQuery<DashboardSlice>;

/// <summary>Sequentially assembles role-aware slices over one scoped local connection.</summary>
public sealed class DashboardComposer(IDashboardQueries queries, Animora.Domain.Time.IClock clock,
    IClinicTimeZoneProvider zones) : IQueryHandler<DashboardSnapshotQuery, DashboardSlice>
{
    public async Task<Result<DashboardSlice>> HandleAsync(DashboardSnapshotQuery query, QueryContext context, CancellationToken ct)
    {
        var date = Animora.Application.Time.ClinicCalendar.LocalDay(clock.UtcNow.UtcTicks, zones.GetClinicTimeZone());
        var today = date.Year * 10000 + date.Month * 100 + date.Day;
        return Result<DashboardSlice>.Ok(await queries.GetSliceAsync(context.TenantId, today, context.UserId, query.Role, ct));
    }
}

public static class DashboardRegistration
{
    public static Animora.Application.Dispatch.ApplicationRegistrationBuilder AddDashboardUseCases(
        this Animora.Application.Dispatch.ApplicationRegistrationBuilder builder) => builder
        .AddQuery<DashboardSnapshotQuery, DashboardSlice, DashboardComposer>();
}
