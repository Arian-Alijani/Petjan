using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Contracts.Navigation;
using Animora.Domain.Results;
using Animora.Domain.Identity;
using Animora.Application.Security;
using System.Collections.Frozen;

namespace Animora.Application.UseCases.Dashboard;

public sealed record DashboardActivity(string Kind, string TitleKey, string EntityId, long AmountMinor,
    int AmountSign, string ActorUserId, DateTimeOffset OccurredUtc = default, string ActorName = "", string Status = "");
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
/// <summary>
/// State of the derived finance facts relative to the authoritative tables: how many outbox messages the projection
/// worker has not applied yet and whether any was dead-lettered. Read with the same slice, never inferred.
/// </summary>
public sealed record DashboardProjectionLag(int Pending, int Failed);
/// <summary>
/// One actionable management alert for the manager dashboard (fixed expense due, cheque due/overdue/bounced). Pure data:
/// presentation (icon, Persian wording, Jalali date) is mapped by the client service. <b>Kind</b> is the alert source
/// (<c>RecurringExpense</c> | <c>Cheque</c>), <b>State</b> the source-specific state (<c>DueSoon</c> | <c>Due</c> |
/// <c>Overdue</c> | <c>Bounced</c>) and <b>Severity</b> the urgency tone (<c>Danger</c> | <c>Warn</c>).
/// </summary>
public sealed record DashboardAlertRow(string Kind, string Title, string Subtitle, DateOnly? DueDay, long AmountMinor,
    string Severity, string State = "");
/// <remarks><b>Generation</b> is the tenant source generation read at the start of the same short read transaction as
/// every other member, so the data and its generation always describe one committed state.</remarks>
public sealed record DashboardSlice(IReadOnlyList<DashboardDay> RevenueDays,
    IReadOnlyList<DashboardActivity> Activity, IReadOnlyList<DashboardWorkload> Workloads,
    IReadOnlyDictionary<string, int> Species, int OpenInvoices, int LowStockCount, int QueueCount,
    DashboardActivityTotals? ActivityTotals = null, IReadOnlyList<DashboardAppointment>? Appointments = null,
    DashboardAccess? Access = null, DashboardReceipts? Receipts = null,
    IReadOnlyList<DashboardOperationalRow>? OpenInvoiceRows = null,
    IReadOnlyList<DashboardOperationalRow>? ReadyBillingRows = null,
    IReadOnlyList<DashboardOperationalRow>? ReminderRows = null, int AwaitingPaymentCount = 0,
    DashboardProjectionLag? Projection = null,
    long Generation = 0,
    IReadOnlyList<DashboardAlertRow>? Alerts = null);

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
    IStaffMembershipRepository memberships, IPermissionEvaluator permissions, ILocalInstallationIdentity installation,
    IChequeQueries? cheques = null, IRecurringExpenseQueries? recurring = null) : IQueryHandler<DashboardSnapshotQuery, DashboardSlice>
{
    public async Task<Result<DashboardSlice>> HandleAsync(DashboardSnapshotQuery query, QueryContext context, CancellationToken ct)
    {
        var access = await ResolveAccessAsync(query, context, gate, sessions, memberships, permissions, installation, ct);
        ct.ThrowIfCancellationRequested();
        if (access.IsFailure) return Result<DashboardSlice>.Fail(access.Error, access.Detail);
        var window = query.Window;
        if (window is null)
        {
            var zone = zones.GetClinicTimeZone();
            var date = Animora.Application.Time.ClinicCalendar.LocalDay(clock.UtcNow.UtcTicks, zone);
            window = DashboardDayWindow.For(date, zone);
        }
        var slice = await queries.GetSliceAsync(context.TenantId, window, context.UserId, access.Value!, ct);
        var alerts = await BuildAlertsAsync(window, access.Value!, ct);
        return Result<DashboardSlice>.Ok(alerts.Count == 0 ? slice : slice with { Alerts = alerts });
    }

    /// <summary>Most alerts the dashboard card ever receives (the list is a glance, not a worklist).</summary>
    internal const int MaxAlerts = 8;

    /// <summary>
    /// Append-only aggregator of management alerts: every source adds rows only when the viewer holds its permission
    /// (<c>finance.read</c> → fixed expenses, <c>cheques.read</c> → cheques), then the whole list is ordered by urgency
    /// (danger before warn, then the earliest due day). A new alert kind plugs in as one more gated block.
    /// </summary>
    private async Task<IReadOnlyList<DashboardAlertRow>> BuildAlertsAsync(DashboardDayWindow window, DashboardAccess access,
        CancellationToken ct)
    {
        var rows = new List<DashboardAlertRow>();
        var today = new DateOnly(window.LocalDay / 10000, window.LocalDay / 100 % 100, window.LocalDay % 100);

        if (access.Finance && recurring is not null)
        {
            var list = await Finance.RecurringExpensePeriods.BuildAsync(recurring, clock, zones, ct);
            foreach (var item in list.Items.Where(i => i.NeedsAttention))
            {
                var (state, severity) = item.State switch
                {
                    Domain.Finance.RecurringDueState.Overdue => ("Overdue", "Danger"),
                    Domain.Finance.RecurringDueState.Due => ("Due", "Warn"),
                    _ => ("DueSoon", "Warn"),
                };
                rows.Add(new DashboardAlertRow("RecurringExpense", item.Title, item.SupplierName ?? string.Empty,
                    item.DueDay, item.AmountMinor, severity, state));
            }
        }

        if (access.Cheques && cheques is not null)
        {
            foreach (var alert in await cheques.GetAlertsAsync(today, ct))
            {
                var severity = alert.Kind is "Bounced" or "Overdue" ? "Danger" : "Warn";
                rows.Add(new DashboardAlertRow("Cheque", string.Empty, string.Empty, alert.DueDay, alert.AmountMinor,
                    severity, alert.Kind));
            }
        }

        return rows.OrderBy(r => r.Severity == "Danger" ? 0 : 1).ThenBy(r => r.DueDay ?? DateOnly.MaxValue)
            .ThenBy(r => r.Kind, StringComparer.Ordinal).Take(MaxAlerts).ToArray();
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
        // One stored-membership/role read for every widget permission (not one per code).
        var allowed = await permissions.AllowedAsync([PermissionCodes.FinanceRead, PermissionCodes.PaymentsRegister,
            PermissionCodes.InventoryRead, PermissionCodes.SalesRead, PermissionCodes.VisitsRead,
            PermissionCodes.VaccinationRead, PermissionCodes.ChequesRead, PermissionCodes.AppointmentsRead,
            PermissionCodes.PatientsRead], subject, ct);
        var finance = allowed.Contains(PermissionCodes.FinanceRead);
        var collections = finance || allowed.Contains(PermissionCodes.PaymentsRegister);
        var inventory = allowed.Contains(PermissionCodes.InventoryRead);
        var sales = allowed.Contains(PermissionCodes.SalesRead);
        var visits = allowed.Contains(PermissionCodes.VisitsRead);
        var vaccination = allowed.Contains(PermissionCodes.VaccinationRead);
        var cheques = allowed.Contains(PermissionCodes.ChequesRead);
        var appointments = allowed.Contains(PermissionCodes.AppointmentsRead);
        var patients = allowed.Contains(PermissionCodes.PatientsRead);
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
