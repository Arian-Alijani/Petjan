using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Contracts.Navigation;
using Animora.Domain.Results;
using Animora.Domain.Identity;
using Animora.Application.Security;
using System.Collections.Frozen;

namespace Animora.Application.UseCases.Dashboard;

public sealed record DashboardActivity(string Kind, string TitleKey, string EntityId, long AmountMinor,
    int AmountSign, string ActorUserId, DateTimeOffset OccurredUtc = default, string ActorName = "");
public sealed record DashboardDay(int LocalDay, long RevenueMinor);
public sealed record DashboardWorkload(string DoctorId, int Total, string Name = "", int Done = 0);
public sealed record DashboardAppointment(string PetName, string OwnerName, string DoctorName,
    DateTimeOffset StartUtc, string State);
/// <summary>Full-day aggregates, independent of the bounded recent-activity preview.</summary>
public sealed record DashboardActivityTotals(long Payments, int Visits, int Vaccinations, int Cheques);
/// <summary>Signed receipts in the product currency; adjustments are not counted a second time.</summary>
public sealed record DashboardReceipts(long Gross, long Refunds, string Currency)
{
    public long Net => checked(Gross - Refunds);
}
public sealed record DashboardOperationalRow(string Kind, string Name, string OwnerName,
    DateTimeOffset AtUtc, string State, string Reference = "");
public sealed record DashboardSlice(IReadOnlyList<DashboardDay> RevenueDays,
    IReadOnlyList<DashboardActivity> Activity, IReadOnlyList<DashboardWorkload> Workloads,
    IReadOnlyDictionary<string, int> Species, int OpenInvoices, int LowStockCount, int QueueCount,
    DashboardActivityTotals? ActivityTotals = null, IReadOnlyList<DashboardAppointment>? Appointments = null,
    DashboardAccess? Access = null, DashboardReceipts? Receipts = null,
    IReadOnlyList<DashboardOperationalRow>? OpenInvoiceRows = null,
    IReadOnlyList<DashboardOperationalRow>? ReadyBillingRows = null,
    IReadOnlyList<DashboardOperationalRow>? ReminderRows = null, int AwaitingPaymentCount = 0);

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

/// <summary>Effective data permissions; the requested presentation role never grants access.</summary>
public sealed record DashboardAccess(UserRole Role, bool Finance, bool Collections, bool Inventory,
    bool Sales, bool Visits, bool Vaccination, bool Cheques, bool Appointments, bool Patients, bool Workloads)
{
    public string CacheKey => $"{Role}:{Finance}:{Collections}:{Inventory}:{Sales}:{Visits}:{Vaccination}:{Cheques}:{Appointments}:{Patients}:{Workloads}";
}

public interface IDashboardQueries
{
    Task<string> VersionAsync(string tenantId, CancellationToken ct);
    Task<DashboardSlice> GetSliceAsync(string tenantId, DashboardDayWindow window, string userId, DashboardAccess access, CancellationToken ct);
}

public sealed record DashboardSnapshotQuery(UserRole Role, DashboardDayWindow? Window = null) : IQuery<DashboardSlice>;

/// <summary>Sequentially assembles role-aware slices over one scoped local connection.</summary>
public sealed class DashboardComposer(IDashboardQueries queries, Animora.Domain.Time.IClock clock,
    IClinicTimeZoneProvider zones, IAuthorizationGate gate, SessionContextCache sessions,
    IStaffMembershipRepository memberships, IPermissionEvaluator permissions, ILocalInstallationIdentity installation) : IQueryHandler<DashboardSnapshotQuery, DashboardSlice>
{
    public async Task<Result<DashboardSlice>> HandleAsync(DashboardSnapshotQuery query, QueryContext context, CancellationToken ct)
    {
        var access = await ResolveAccessAsync(query, context, gate, sessions, memberships, permissions, installation, ct);
        if (access.IsFailure) return Result<DashboardSlice>.Fail(access.Error, access.Detail);
        var window = query.Window;
        if (window is null)
        {
            var zone = zones.GetClinicTimeZone();
            var date = Animora.Application.Time.ClinicCalendar.LocalDay(clock.UtcNow.UtcTicks, zone);
            window = DashboardDayWindow.For(date, zone);
        }
        return Result<DashboardSlice>.Ok(await queries.GetSliceAsync(context.TenantId, window, context.UserId, access.Value!, ct));
    }

    // Both cache responses and dispatcher reads use this live policy. No TTL-based permission cache.
    public static async Task<Result<DashboardAccess>> ResolveAccessAsync(DashboardSnapshotQuery query,
        QueryContext context, IAuthorizationGate gate, SessionContextCache sessions,
        IStaffMembershipRepository memberships, IPermissionEvaluator permissions,
        ILocalInstallationIdentity installation, CancellationToken ct)
    {
        var admitted = await gate.EvaluateAsync(query, context, ct);
        if (admitted.IsFailure) return Result<DashboardAccess>.Fail(admitted.Error, admitted.Detail);
        var member = await memberships.FindActiveAsync(context.TenantId, context.UserId, ct);
        if (member is not { IsActive: true } || !Enum.IsDefined(member.Role)
            || member.Role != sessions.Current?.Role
            || member.TenantId != context.TenantId || member.UserId != context.UserId)
            return Result<DashboardAccess>.Fail(ErrorCodes.PermissionDenied);
        var subject = new AuthorizationSubject(context.TenantId, context.UserId, FrozenSet<string>.Empty,
            member.Role, installation.DeviceId, IsOffline: false);
        async Task<bool> Can(string code) => (await permissions.EvaluateAsync(code, subject, null, ct)).Allowed;
        var finance = await Can(PermissionCodes.FinanceRead);
        var collections = finance || await Can(PermissionCodes.PaymentsRegister);
        var inventory = await Can(PermissionCodes.InventoryRead);
        var sales = await Can(PermissionCodes.SalesRead);
        var visits = await Can(PermissionCodes.VisitsRead);
        var vaccination = await Can(PermissionCodes.VaccinationRead);
        var cheques = await Can(PermissionCodes.ChequesRead);
        var appointments = await Can(PermissionCodes.AppointmentsRead);
        var patients = await Can(PermissionCodes.PatientsRead);
        var role = member.Role switch
        {
            StaffRole.Doctor => UserRole.Doctor,
            StaffRole.Cashier => UserRole.Cashier,
            StaffRole.Receptionist => UserRole.Receptionist,
            _ => UserRole.Manager,
        };
        return Result<DashboardAccess>.Ok(new(role, finance, collections, inventory, sales, visits,
            vaccination, cheques, appointments, patients, member.Role == StaffRole.Manager && appointments));
    }
}

public static class DashboardRegistration
{
    public static Animora.Application.Dispatch.ApplicationRegistrationBuilder AddDashboardUseCases(
        this Animora.Application.Dispatch.ApplicationRegistrationBuilder builder) => builder
        .AddQuery<DashboardSnapshotQuery, DashboardSlice, DashboardComposer>();
}
