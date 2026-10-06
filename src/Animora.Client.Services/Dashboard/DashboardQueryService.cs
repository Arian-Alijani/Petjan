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
using Microsoft.Extensions.Logging;
using Animora.Application.Correlation;

namespace Animora.Client.Services.Dashboard;

/// <summary>
/// 60-second tenant/user/effective-permission/day/source-generation cache. Live authorization precedes every hit; the
/// generation (<see cref="IDashboardQueries.VersionAsync"/>) advances on any committed change to a source table, so
/// data written without a new feed event (facts, rebuilds, appointments, stock, patients) never serves a stale hit.
/// </summary>
public sealed class DashboardQueryService(IServiceScopeFactory scopes) : IDashboardQueryService
{
    private readonly TenantScopedCache<string, (DateTimeOffset Expires, DashboardSnapshot Snapshot)> _cache = new();
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    // Fixed-expense payments and expenses are not part of the source generation, so a snapshot carrying management alerts
    // is only briefly reusable: paying a due item clears its alert on the very next read instead of up to a minute later.
    private const int AlertCacheSeconds = 5;

    // Microsoft.Data.Sqlite executes async reads synchronously. Keep the whole scoped read/mapping
    // off the UI thread, not just the continuation after its first SQL call.
    public Task<DashboardSnapshot> GetSnapshotAsync(UserRole role, CancellationToken ct = default) => ReadAsync(role, false, ct);

    public Task<DashboardSnapshot> RefreshSnapshotAsync(UserRole role, CancellationToken ct = default) => ReadAsync(role, true, ct);

    private Task<DashboardSnapshot> ReadAsync(UserRole role, bool force, CancellationToken ct) => Task.Run(async () =>
    {
        // WaitAsync(ct) throws before acquiring, so the release below only ever pairs with a real acquire.
        await _loadGate.WaitAsync(ct).ConfigureAwait(false);
        try { return await LoadSnapshotAsync(role, force, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (DashboardQueryException) { throw; }
        catch (Exception ex)
        {
            using var scope = scopes.CreateScope();
            throw Failure(scope.ServiceProvider, ErrorMapping.FromException(ex).Code);
        }
        finally { _loadGate.Release(); }
    }, ct);

    private async Task<DashboardSnapshot> LoadSnapshotAsync(UserRole role, bool force, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var context = provider.BuildQueryContext();
        if (context.IsFailure) throw Failure(scope.ServiceProvider, context.Error.Code);
        var policy = await DashboardComposer.ResolveAccessAsync(new DashboardSnapshotQuery(role), context.Value!,
            scope.ServiceProvider.GetRequiredService<IAuthorizationGate>(),
            scope.ServiceProvider.GetRequiredService<SessionContextCache>(),
            scope.ServiceProvider.GetRequiredService<IStaffMembershipRepository>(),
            scope.ServiceProvider.GetRequiredService<IPermissionEvaluator>(),
            scope.ServiceProvider.GetRequiredService<ILocalInstallationIdentity>(), ct);
        if (policy.IsFailure) throw Failure(scope.ServiceProvider, policy.Error.Code);
        var access = policy.Value!;
        ct.ThrowIfCancellationRequested();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        var zone = scope.ServiceProvider.GetRequiredService<IClinicTimeZoneProvider>().GetClinicTimeZone();
        var window = DashboardDayWindow.For(ClinicCalendar.LocalDay(now.UtcTicks, zone), zone);
        var queries = scope.ServiceProvider.GetRequiredService<IDashboardQueries>();
        var version = await queries.VersionAsync(context.Value!.TenantId, ct);
        string KeyFor(string generation) =>
            $"{context.Value.TenantId}|{context.Value.UserId}|dashboard:{access.CacheKey}|day{window.LocalDay}|v{generation}";
        var key = KeyFor(version);
        if (!force && _cache.TryGet(key, out var entry) && entry.Expires > now) return entry.Snapshot;
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
            .QueryAsync(new DashboardSnapshotQuery(role, window), context.Value, ct);
        if (result.IsFailure) throw Failure(scope.ServiceProvider, result.Error.Code);
        var snapshot = DashboardMapping.Map(access.Role, result.Value!, zone) with
        {
            Freshness = result.Value!.Projection is { } lag ? new DashboardFreshness(now, lag.Pending, lag.Failed > 0) : null,
        };
        // The slice carries the generation of the very state it was read from; if a write landed between the probe
        // and the read, the entry is filed under that newer generation, never under the stale probe value.
        _cache.Set(KeyFor(result.Value!.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            (now.AddSeconds(result.Value!.Alerts is { Count: > 0 } ? AlertCacheSeconds : 60), snapshot));
        return snapshot;
    }
    private static DashboardQueryException Failure(IServiceProvider services, string code)
    {
        var kind = DashboardQueryException.Classify(code);
        var correlation = services.GetService<ICorrelationContext>()?.CurrentId ?? Guid.NewGuid().ToString("N");
        // Whitelisted category + opaque correlation only: no exception, payload, identity or SQL in this log.
        services.GetService<ILogger<DashboardQueryService>>()?.LogWarning(
            "Dashboard read failed: {FailureKind}; correlation {CorrelationId}", kind, correlation);
        return new DashboardQueryException(kind, correlation);
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
                Stat("IconClock", "صف فعال من", slice.QueueCount), Stat("IconEdit", "در انتظار", slice.QueueCount),
                Stat("IconSyringe", "واکسن‌ها", totals.Vaccinations) },
            UserRole.Cashier => new[] { Stat("IconCart", "خالص تسویه غیرنسیه امروز (تومان)", payment) with
                { Foot = slice.Receipts is { } receipt ? $"دریافت ناخالص: {receipt.Gross:N0} · بازپرداخت: {receipt.Refunds:N0} تومان" : null },
                Stat("IconReceipt", "فاکتورهای باز (شامل پیش‌نویس)", slice.OpenInvoices), Stat("IconWallet", "در انتظار پرداخت", slice.AwaitingPaymentCount),
                Stat("IconCheque", "تغییر وضعیت چک امروز", totals.Cheques) },
            _ => new[] { Stat("IconArrowUpRight", "درآمد امروز", today),
                Stat("IconUsers", "ویزیت امروز", visits), RemainingAppointments(slice),
                Stat("IconBox", "هشدار موجودی", slice.LowStockCount) },
        };
        if (slice.Access is { } allowed)
            stats = stats.Where(stat => stat.IconKey switch
            {
                "IconArrowUpRight" => allowed.Finance,
                "IconCart" => allowed.Collections,
                "IconReceipt" or "IconWallet" => allowed.Sales,
                "IconBox" => allowed.Inventory,
                "IconUsers" or "IconCheck" => allowed.Visits,
                "IconClock" or "IconEdit" or "IconClipboard" => allowed.Appointments,
                "IconSyringe" => allowed.Vaccination,
                "IconCheque" => allowed.Cheques,
                _ => false,
            }).ToArray();
        zone ??= TimeZoneInfo.Utc;
        var primary = role == UserRole.Cashier
            ? (slice.OpenInvoiceRows ?? []).Select(x => OperationalItem(x, zone)).ToArray()
            : (slice.Appointments ?? []).Select(x => AppointmentItem(x, zone)).ToArray();
        var secondary = role switch
        {
            UserRole.Cashier => (slice.ReadyBillingRows ?? []).Select(x => OperationalItem(x, zone)).ToArray(),
            UserRole.Doctor => (slice.ReminderRows ?? []).Select(x => OperationalItem(x, zone)).ToArray(),
            _ => MergeAlerts(slice, zone),
        };
        var manager = role is UserRole.Manager or UserRole.Receptionist;
        var values = slice.RevenueDays.Select(x => x.RevenueMinor).ToArray();
        var (points, zeroLevel) = Normalize(values);
        return new DashboardSnapshot(role, "نمای کلی امروز", "آخرین فعالیت‌های کلینیک", stats,
            role == UserRole.Cashier ? "فاکتورهای باز" : "نوبت‌های فعال امروز", primary,
            role switch
            {
                UserRole.Cashier => "آماده صدور فاکتور",
                UserRole.Doctor => "یادآور واکسن بیماران امروز",
                _ => slice.Alerts is { Count: > 0 } ? "هشدارها و فعالیت‌های اخیر" : "فعالیت‌های اخیر",
            }, secondary,
            (slice.Access?.Finance ?? manager) ? Pulse(slice.RevenueDays, today, points, zeroLevel, values) : null,
            (slice.Access?.Workloads ?? manager) ? slice.Workloads.Select(Load).ToArray() : null,
            (slice.Access?.Patients ?? manager) && role != UserRole.Doctor ? slice.Species.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new DashboardSpecies(SpeciesName(x.Key), x.Value, SpeciesColor(x.Key), x.Key)).ToArray() : null);
    }

    private const string CurrencyLabel = "تومان";

    // Alerts (already permission-gated and urgency-ordered by the Application read) lead the card; the remaining room is
    // filled with the latest activity. Presentation only: no permission or port access here.
    private static DashboardListItem[] MergeAlerts(DashboardSlice slice, TimeZoneInfo zone)
    {
        const int capacity = 8;
        var alerts = (slice.Alerts ?? []).Take(capacity).Select(AlertItem).ToList();
        alerts.AddRange(slice.Activity.Take(capacity - alerts.Count).Select(x => Item(x, zone)));
        return alerts.ToArray();
    }

    internal static DashboardListItem AlertItem(DashboardAlertRow alert)
    {
        var tone = alert.Severity == "Danger" ? DashboardTone.Danger : DashboardTone.Warn;
        var amount = alert.AmountMinor.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " " + CurrencyLabel;
        var due = alert.DueDay is { } day ? JalaliDay(day) : string.Empty;
        var dueLine = string.Join(" · ", new[] { due.Length == 0 ? "" : "سررسید " + due, amount }.Where(v => v.Length > 0));
        if (alert.Kind == "Cheque")
        {
            var (title, tag) = alert.State switch
            {
                "Bounced" => ("چک برگشتی نیازمند پیگیری", "برگشتی"),
                "Overdue" => ("چک سررسیدگذشته تسویه نشده", "معوق"),
                _ => ("چک نزدیک به سررسید", "سررسید نزدیک"),
            };
            return new("", title, dueLine, tag, tone, "IconCheque", "AlertCheque", alert.State);
        }

        var (headline, tagText) = alert.State switch
        {
            "Overdue" => ($"{alert.Title} معوق است", "معوق"),
            "Due" => ($"{alert.Title} امروز سررسید است", "سررسید امروز"),
            _ => ($"{alert.Title} نزدیک است", "سررسید پرداخت"),
        };
        var subtitle = string.IsNullOrWhiteSpace(alert.Subtitle) ? dueLine : $"{dueLine} · {alert.Subtitle}";
        return new("", headline, subtitle, tagText, tone, "IconWallet", "AlertRecurring", alert.State);
    }

    private static string JalaliDay(DateOnly day)
    {
        var calendar = new System.Globalization.PersianCalendar();
        var at = day.ToDateTime(TimeOnly.MinValue);
        return $"{calendar.GetYear(at):0000}/{calendar.GetMonth(at):00}/{calendar.GetDayOfMonth(at):00}";
    }

    // Headline = TODAY's revenue; the chart covers the real 14-day window. Dates and signed amounts stay in the DTO.
    private static DashboardRevenuePulse Pulse(IReadOnlyList<DashboardDay> days, long today, double[] points, double zero, long[] values)
    {
        var calendar = new System.Globalization.PersianCalendar();
        static string Money(long v) => v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        string Label(int day)
        {
            var date = new DateTime(day / 10000, day / 100 % 100, day % 100, 0, 0, 0, DateTimeKind.Unspecified);
            return $"{calendar.GetYear(date):0000}/{calendar.GetMonth(date):00}/{calendar.GetDayOfMonth(date):00}";
        }
        var labels = days.Select(d => Label(d.LocalDay)).ToArray();
        var summary = days.Count == 0 ? "داده‌ای برای نمایش وجود ندارد" :
            $"{labels[0]} تا {labels[^1]} · کمترین {Money(values.Min())} · بیشترین {Money(values.Max())} {CurrencyLabel}";
        return new DashboardRevenuePulse(today.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
            $"درآمد امروز ({CurrencyLabel})", points)
        { ZeroLevel = zero, Values = values, DayLabels = labels, Summary = summary };
    }

    /// <summary>
    /// Signed normalization: the domain always contains zero, so a negative day sits below the zero level and
    /// an all-negative/all-zero series divides by a non-zero span or yields a flat series — never NaN/Infinity.
    /// </summary>
    internal static (double[] Points, double ZeroLevel) Normalize(IReadOnlyList<long> values)
    {
        if (values.Count == 0) return ([], 0);
        double low = Math.Min(0, values.Min()), high = Math.Max(0, values.Max());
        var span = high - low;
        if (span <= 0) return (new double[values.Count], 0);
        return (values.Select(v => (v - low) / span).ToArray(), (0 - low) / span);
    }

    // Specialty is not modelled, so it is left empty rather than invented. A removed/unnamed doctor gets an
    // explicit label; the avatar initial is the first text element (never half of a surrogate pair).
    internal const string UnknownDoctorName = "پزشک نامشخص";
    private static DashboardDoctorLoad Load(DashboardWorkload x)
    {
        var name = string.IsNullOrWhiteSpace(x.Name) ? UnknownDoctorName : x.Name.Trim();
        return new(System.Globalization.StringInfo.GetNextTextElement(name), name, string.Empty,
            Math.Clamp(x.Done, 0, x.Total), x.Total, "#0D9488");
    }

    // Manager "today's pace" card: the live active queue (waiting / called / in-visit), with the completion rate from the workload rows
    // already in the slice (no extra query). Gated like every other appointment stat (IconClock => Appointments).
    private static DashboardStat RemainingAppointments(DashboardSlice slice)
    {
        var total = slice.Workloads.Sum(w => w.Total);
        var done = slice.Workloads.Sum(w => Math.Clamp(w.Done, 0, w.Total));
        var stat = Stat("IconClock", "صف فعال امروز", slice.QueueCount);
        if (total <= 0) return stat;
        var percent = (int)Math.Round(done * 100.0 / total, MidpointRounding.AwayFromZero);
        return stat with
        {
            Delta = $"{percent.ToString(System.Globalization.CultureInfo.InvariantCulture)}% انجام‌شده",
            Foot = $"{done.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} از {total.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} نوبت امروز انجام شده",
        };
    }

    private static DashboardStat Stat(string icon, string label, long value) =>
        new(icon, label, value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), null, true, StatTone(icon));

    // Each KPI family gets its own accent so the stat row reads at a glance (money = brand, people = info,
    // queue/time = lilac, stock/pending = warn, cheques = peach).
    private static DashboardTone StatTone(string icon) => icon switch
    {
        "IconUsers" or "IconCheck" => DashboardTone.Info,
        "IconClock" or "IconEdit" => DashboardTone.Lilac,
        "IconBox" or "IconWallet" => DashboardTone.Warn,
        "IconCheque" or "IconSyringe" => DashboardTone.Peach,
        _ => DashboardTone.Brand,
    };
    private static DashboardListItem Item(DashboardActivity x, TimeZoneInfo zone) =>
        new("", SafeActivityTitle(x), string.Join(" · ", new[] { TimeZoneInfo.ConvertTime(x.OccurredUtc, zone).ToString("HH:mm"),
                x.ActorName, x.AmountMinor == 0 ? "" : x.AmountMinor.ToString("N0") + " تومان" }.Where(v => v.Length > 0)),
            KindName(x.Kind), x.AmountSign < 0 ? DashboardTone.Warn : DashboardTone.Info, Icon(x.Kind),
            x.Kind, x.Status, x.TitleKey);

    // The translation key travels separately; the fallback title is Persian text, never the technical key.
    private static string SafeActivityTitle(DashboardActivity x) => $"رویداد {KindName(x.Kind)}";

    private static DashboardListItem OperationalItem(DashboardOperationalRow row, TimeZoneInfo zone) =>
        new("", string.IsNullOrWhiteSpace(row.Name) ? "بدون عنوان" : row.Name,
            $"{TimeZoneInfo.ConvertTime(row.AtUtc, zone):MM/dd HH:mm} · " +
            (string.IsNullOrWhiteSpace(row.OwnerName) ? "مالک نامشخص" : row.OwnerName) +
            (row.Kind == "Vaccine" && !string.IsNullOrWhiteSpace(row.Reference) ? $" · {row.Reference}" : ""),
            row.State switch { "Draft" => "پیش‌نویس", "Posted" => "در انتظار پرداخت", "PartiallyPaid" => "پرداخت جزئی",
                "Ready" => "آماده صدور", "Due" => "موعد واکسن", _ => "عملیات" },
            row.State is "Due" or "PartiallyPaid" ? DashboardTone.Warn : DashboardTone.Info,
            row.Kind switch { "Invoice" => "IconReceipt", "Visit" => "IconClipboard", "Boarding" => "IconBriefcase", "Vaccine" => "IconSyringe", _ => "IconClipboard" },
            row.Kind, row.State);

    private static DashboardListItem AppointmentItem(DashboardAppointment x, TimeZoneInfo zone) =>
        new("", string.IsNullOrWhiteSpace(x.PetName) ? "بیمار نامشخص" : x.PetName,
            $"{TimeZoneInfo.ConvertTime(x.StartUtc, zone):HH:mm} · {x.OwnerName} · {x.DoctorName}",
            x.State switch { "Scheduled" => "رزرو شده", "Confirmed" => "تأیید شده", "CheckedIn" => "پذیرش شده",
                "InVisit" => "در حال ویزیت", "Completed" => "تکمیل شده", "Cancelled" => "لغو شده",
                "NoShow" => "عدم حضور", _ => "نوبت" },
            x.State == "Completed" ? DashboardTone.Ok : x.State is "Cancelled" or "NoShow" ? DashboardTone.Warn : DashboardTone.Info,
            "IconCalendar", "Appointment", x.State);

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
        "Cat" => "#0D9488", "Dog" => "#6366F1", "Bird" => "#F59E0B", "Rabbit" => "#EC4899",
        "Rodent" => "#8B5CF6", "Reptile" => "#16A34A", _ => "#64748B"
    };
}
