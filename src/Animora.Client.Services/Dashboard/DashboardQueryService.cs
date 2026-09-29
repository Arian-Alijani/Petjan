using Animora.Application.Abstractions;
using Animora.Application.Security;
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

    public async Task<DashboardSnapshot> GetSnapshotAsync(UserRole role)
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var context = provider.BuildQueryContext();
        if (context.IsFailure) return DashboardMapping.Empty(role);
        var queries = scope.ServiceProvider.GetRequiredService<IDashboardQueries>();
        var version = await queries.VersionAsync(context.Value!.TenantId, default);
        var key = $"{context.Value.TenantId}|{context.Value.UserId}|dashboard:{role}|v{version}";
        if (_cache.TryGet(key, out var entry) && entry.Expires > DateTimeOffset.UtcNow) return entry.Snapshot;
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
            .QueryAsync(new DashboardSnapshotQuery(role), context.Value);
        if (result.IsFailure) return DashboardMapping.Empty(role);
        var snapshot = DashboardMapping.Map(role, result.Value!);
        _cache.Set(key, (DateTimeOffset.UtcNow.AddSeconds(60), snapshot));
        return snapshot;
    }
}

internal static class DashboardMapping
{
    internal static DashboardSnapshot Empty(UserRole role) => Map(role,
        new DashboardSlice([], [], [], new Dictionary<string, int>(), 0, 0, 0));

    internal static DashboardSnapshot Map(UserRole role, DashboardSlice slice)
    {
        var today = slice.RevenueDays.Count == 0 ? 0 : slice.RevenueDays[^1].RevenueMinor;
        var payment = slice.Activity.Where(x => x.TitleKey == "history.payment.registered").Sum(x => x.AmountMinor);
        var visits = slice.Activity.Count(x => x.TitleKey == "history.visit.completed");
        var stats = role switch
        {
            UserRole.Doctor => new[] { Stat("IconCheck", "ویزیت‌های من", visits),
                Stat("IconClock", "صف من", slice.QueueCount), Stat("IconEdit", "در انتظار", slice.QueueCount),
                Stat("IconSyringe", "واکسن‌ها", slice.Activity.Count(x => x.TitleKey == "history.vaccine.administered")) },
            UserRole.Cashier => new[] { Stat("IconCart", "دریافت امروز", payment),
                Stat("IconReceipt", "فاکتورهای باز", slice.OpenInvoices), Stat("IconClipboard", "صف پرداخت", slice.QueueCount),
                Stat("IconCheque", "چک‌ها", slice.Activity.Count(x => x.Kind == "Cheque")) },
            _ => new[] { Stat("IconArrowUpRight", "درآمد امروز", today),
                Stat("IconUsers", "ویزیت امروز", visits), Stat("IconReceipt", "فاکتورهای باز", slice.OpenInvoices),
                Stat("IconBox", "هشدار موجودی", slice.LowStockCount) },
        };
        var primary = slice.Activity.Where(x => role switch
            {
                UserRole.Doctor => x.Kind is "Appointment" or "Visit",
                UserRole.Cashier => x.Kind == "Sale",
                _ => x.Kind == "Appointment",
            }).Take(8).Select(Item).ToArray();
        var secondary = slice.Activity.Where(x => role switch
            {
                UserRole.Doctor => x.Kind is "Vaccine" or "Visit",
                UserRole.Cashier => x.TitleKey == "history.payment.registered",
                _ => x.Kind is "Inventory" or "Cheque" or "Sale",
            }).Take(8).Select(Item).ToArray();
        var manager = role is UserRole.Manager or UserRole.Receptionist;
        var values = slice.RevenueDays.Select(x => x.RevenueMinor).ToArray();
        var max = values.Length == 0 ? 0 : values.Max();
        return new DashboardSnapshot(role, "نمای کلی امروز", "آخرین فعالیت‌های کلینیک", stats,
            role == UserRole.Cashier ? "فاکتورهای امروز" : "نوبت‌های امروز", primary,
            "رویدادهای اخیر", secondary,
            manager ? new DashboardRevenuePulse(today.ToString("N0"), "۱۴ روز اخیر",
                values.Select(x => max == 0 ? 0d : (double)x / max).ToArray()) : null,
            manager ? slice.Workloads.Select(x => new DashboardDoctorLoad("پ", x.DoctorId, "پزشک",
                0, x.Total, "#0D9488")).ToArray() : null,
            manager ? slice.Species.Select(x => new DashboardSpecies(x.Key, x.Value, "#0D9488")).ToArray() : null);
    }

    private static DashboardStat Stat(string icon, string label, long value) =>
        new(icon, label, value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), null, true);
    private static DashboardListItem Item(DashboardActivity x) =>
        new("•", x.TitleKey.Replace("history.", "", StringComparison.Ordinal), x.EntityId,
            x.Kind, DashboardTone.Info);
}
