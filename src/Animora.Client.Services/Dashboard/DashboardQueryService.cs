using Animora.Application.Abstractions;
using Animora.Application.Security;
using Animora.Application.Ports;
using Animora.Application.Time;
using Animora.Domain.Time;
using Animora.Application.UseCases.Dashboard;
using Animora.Client.Services.Caching;
using Animora.Contracts.Dashboard;
using Animora.Contracts.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Dashboard;

/// <summary>60-second tenant/user/role/version cache over role-specific persisted slices.</summary>
public sealed class DashboardQueryService(IServiceScopeFactory scopes) : IDashboardQueryService
{
    private readonly TenantScopedCache<string, (DateTimeOffset Expires, DashboardSnapshot Snapshot)> _cache = new();
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    // Microsoft.Data.Sqlite executes async reads synchronously. Keep the whole scoped read/mapping
    // off the UI thread, not just the continuation after its first SQL call.
    public Task<DashboardSnapshot> GetSnapshotAsync(UserRole role) => Task.Run(async () =>
    {
        await _loadGate.WaitAsync().ConfigureAwait(false);
        try { return await LoadSnapshotAsync(role).ConfigureAwait(false); }
        finally { _loadGate.Release(); }
    });

    private async Task<DashboardSnapshot> LoadSnapshotAsync(UserRole role)
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var context = provider.BuildQueryContext();
        if (context.IsFailure) throw new InvalidOperationException(context.Error.Code);
        var policy = await DashboardComposer.ResolveAccessAsync(new DashboardSnapshotQuery(role), context.Value!,
            scope.ServiceProvider.GetRequiredService<IAuthorizationGate>(),
            scope.ServiceProvider.GetRequiredService<SessionContextCache>(),
            scope.ServiceProvider.GetRequiredService<IStaffMembershipRepository>(),
            scope.ServiceProvider.GetRequiredService<IPermissionEvaluator>(),
            scope.ServiceProvider.GetRequiredService<ILocalInstallationIdentity>(), default);
        if (policy.IsFailure) throw new InvalidOperationException(policy.Error.Code);
        var access = policy.Value!;
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        var zone = scope.ServiceProvider.GetRequiredService<IClinicTimeZoneProvider>().GetClinicTimeZone();
        var window = DashboardDayWindow.For(ClinicCalendar.LocalDay(now.UtcTicks, zone), zone);
        var queries = scope.ServiceProvider.GetRequiredService<IDashboardQueries>();
        var version = await queries.VersionAsync(context.Value!.TenantId, default);
        var key = $"{context.Value.TenantId}|{context.Value.UserId}|dashboard:{access.CacheKey}|day{window.LocalDay}|v{version}";
        if (_cache.TryGet(key, out var entry) && entry.Expires > now) return entry.Snapshot;
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
            .QueryAsync(new DashboardSnapshotQuery(role, window), context.Value);
        if (result.IsFailure) throw new InvalidOperationException(result.Error.Code);
        var snapshot = DashboardMapping.Map(access.Role, result.Value!, zone);
        _cache.Set(key, (now.AddSeconds(60), snapshot));
        return snapshot;
    }
}

internal static class DashboardMapping
{
    internal static DashboardSnapshot Empty(UserRole role) => Map(role,
        new DashboardSlice([], [], [], new Dictionary<string, int>(), 0, 0, 0));

    internal static DashboardSnapshot Map(UserRole role, DashboardSlice slice, TimeZoneInfo? zone = null)
    {
        var today = slice.RevenueDays.Count == 0 ? 0 : slice.RevenueDays[^1].RevenueMinor;
        var totals = slice.ActivityTotals ?? new DashboardActivityTotals(0, 0, 0, 0);
        var payment = totals.Payments;
        var visits = totals.Visits;
        var stats = role switch
        {
            UserRole.Doctor => new[] { Stat("IconCheck", "ویزیت‌های من", visits),
                Stat("IconClock", "صف من", slice.QueueCount), Stat("IconEdit", "در انتظار", slice.QueueCount),
                Stat("IconSyringe", "واکسن‌ها", totals.Vaccinations) },
            UserRole.Cashier => new[] { Stat("IconCart", "دریافت امروز", payment),
                Stat("IconReceipt", "فاکتورهای باز", slice.OpenInvoices), Stat("IconClipboard", "صف پرداخت", slice.QueueCount),
                Stat("IconCheque", "چک‌ها", totals.Cheques) },
            _ => new[] { Stat("IconArrowUpRight", "درآمد امروز", today),
                Stat("IconUsers", "ویزیت امروز", visits), Stat("IconReceipt", "فاکتورهای باز", slice.OpenInvoices),
                Stat("IconBox", "هشدار موجودی", slice.LowStockCount) },
        };
        if (slice.Access is { } allowed)
            stats = stats.Where(stat => stat.IconKey switch
            {
                "IconArrowUpRight" => allowed.Finance,
                "IconCart" => allowed.Collections,
                "IconReceipt" => allowed.Sales,
                "IconBox" => allowed.Inventory,
                "IconUsers" or "IconCheck" => allowed.Visits,
                "IconClock" or "IconEdit" or "IconClipboard" => allowed.Appointments,
                "IconSyringe" => allowed.Vaccination,
                "IconCheque" => allowed.Cheques,
                _ => false,
            }).ToArray();
        zone ??= TimeZoneInfo.Utc;
        var primary = role == UserRole.Cashier
            ? slice.Activity.Where(x => x.Kind == "Sale").Take(8).Select(x => Item(x, zone)).ToArray()
            : (slice.Appointments ?? []).Select(x => AppointmentItem(x, zone)).ToArray();
        var secondary = slice.Activity.Where(x => role switch
            {
                UserRole.Doctor => x.Kind is "Vaccine" or "Visit",
                UserRole.Cashier => x.TitleKey == "history.payment.registered",
                _ => true,
            }).Take(8).Select(x => Item(x, zone)).ToArray();
        var manager = role is UserRole.Manager or UserRole.Receptionist;
        var values = slice.RevenueDays.Select(x => x.RevenueMinor).ToArray();
        var max = values.Length == 0 ? 0 : values.Max();
        return new DashboardSnapshot(role, "نمای کلی امروز", "آخرین فعالیت‌های کلینیک", stats,
            role == UserRole.Cashier ? "فاکتورهای امروز" : "نوبت‌های امروز", primary,
            "رویدادهای اخیر", secondary,
            (slice.Access?.Finance ?? manager) ? new DashboardRevenuePulse(today.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "۱۴ روز اخیر",
                values.Select(x => max == 0 ? 0d : (double)x / max).ToArray()) : null,
            (slice.Access?.Workloads ?? manager) ? slice.Workloads.Select(x => new DashboardDoctorLoad(string.IsNullOrEmpty(x.Name) ? "پ" : x.Name[..1], x.Name, "پزشک",
                x.Done, x.Total, "#0D9488")).ToArray() : null,
            (slice.Access?.Patients ?? manager) && role != UserRole.Doctor ? slice.Species.Select(x => new DashboardSpecies(SpeciesName(x.Key), x.Value, SpeciesColor(x.Key))).ToArray() : null);
    }

    private static DashboardStat Stat(string icon, string label, long value) =>
        new(icon, label, value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), null, true);
    private static DashboardListItem Item(DashboardActivity x, TimeZoneInfo zone) =>
        new("", x.TitleKey,
            string.Join(" · ", new[] { TimeZoneInfo.ConvertTime(x.OccurredUtc, zone).ToString("HH:mm"),
                x.ActorName, x.AmountMinor == 0 ? "" : x.AmountMinor.ToString("N0") + " ریال" }.Where(v => v.Length > 0)),
            KindName(x.Kind), x.AmountSign < 0 ? DashboardTone.Warn : DashboardTone.Info, Icon(x.Kind));

    private static DashboardListItem AppointmentItem(DashboardAppointment x, TimeZoneInfo zone) =>
        new("", x.PetName, $"{TimeZoneInfo.ConvertTime(x.StartUtc, zone):HH:mm} · {x.OwnerName} · {x.DoctorName}",
            x.State switch { "Scheduled" => "رزرو شده", "Confirmed" => "تأیید شده", "CheckedIn" => "پذیرش شده",
                "InVisit" => "در حال ویزیت", "Completed" => "تکمیل شده", "Cancelled" => "لغو شده",
                "NoShow" => "عدم حضور", _ => "نوبت" },
            x.State == "Completed" ? DashboardTone.Ok : x.State is "Cancelled" or "NoShow" ? DashboardTone.Warn : DashboardTone.Info,
            "IconCalendar");

    private static string KindName(string kind) => kind switch
    {
        "Patient" => "بیمار", "Appointment" => "نوبت", "Visit" => "ویزیت", "Vaccine" => "واکسن",
        "Boarding" => "پانسیون", "Purchase" => "خرید", "Inventory" => "انبار", "Sale" => "فروش و پرداخت",
        "Cheque" => "چک", "Expense" => "هزینه", _ => "رویداد"
    };
    private static string Icon(string kind) => kind switch
    {
        "Appointment" => "IconCalendar", "Visit" => "IconClipboard", "Vaccine" => "IconSyringe",
        "Boarding" => "IconBriefcase", "Purchase" => "IconCart", "Inventory" => "IconBox",
        "Sale" or "Expense" => "IconReceipt", "Cheque" => "IconCheque", _ => "IconPaw"
    };
    private static string SpeciesName(string species) => species switch
    {
        "Cat" => "گربه", "Dog" => "سگ", "Bird" => "پرنده", "Rabbit" => "خرگوش",
        "Rodent" => "جوندگان", "Reptile" => "خزندگان", _ => "سایر"
    };
    private static string SpeciesColor(string species) => species switch
    {
        "Cat" => "#0D9488", "Dog" => "#6366F1", "Bird" => "#F59E0B", "Rabbit" => "#EC4899", _ => "#64748B"
    };
}
