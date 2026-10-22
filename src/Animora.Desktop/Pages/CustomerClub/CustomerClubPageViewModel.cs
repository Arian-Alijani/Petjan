using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.CustomerClub;
using Animora.Contracts.Diagnostics;
using Animora.Contracts.Identity;
using Animora.Contracts.Statistics;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Dashboard;
using Animora.Desktop.Pages.Statistics;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using Animora.Domain.Identity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.CustomerClub;

/// <summary>One patient whose birthday is today (species look resolved here, never in XAML).</summary>
public sealed record BirthdayRow(string PetName, string Species, string IconKey, string OwnerName, string Phone,
    string Age, string PetId, string OwnerId);

/// <summary>One ranked client of a leaderboard; <see cref="Primary"/> is the ranking metric, <see cref="Secondary"/> the other one.</summary>
public sealed record TopClientRow(string Rank, string Name, string Phone, string Primary, string Secondary,
    string LastVisit, double Fraction);

/// <summary>
/// «باشگاه مشتریان»: today's pet birthdays (with a greeting-SMS affordance that is UI-only for now) plus the
/// top-spending and most-visiting client leaderboards. Birthdays need <c>patients.read</c> (the page gate); the two
/// financial leaderboards additionally need <c>reports.read</c> and are hidden — never queried — without it.
/// The three reads run concurrently and publish together; a failed reload keeps the previous data on screen.
/// </summary>
public sealed partial class CustomerClubPageViewModel : PageViewModelBase
{
    private const int LeaderboardSize = 10;

    private readonly ICustomerClubQueryService _service;
    private readonly IStoreReadinessGate _readiness;
    private readonly IToastService _toast;
    private CancellationTokenSource? _loadCts;
    private int _generation;

    public CustomerClubPageViewModel(ICustomerClubQueryService service, IStoreReadinessGate readiness,
        IToastService toast, IDataChangeNotifier changes, ICapabilityGate capabilities)
    {
        _service = service;
        _readiness = readiness;
        _toast = toast;
        CanSeeFinancials = capabilities.Can(PermissionCodes.ReportsRead);

        Title = "باشگاه مشتریان";
        Eyebrow = "وفاداری و ارتباط با مشتری";
        Breadcrumbs.Add("مدیریت کلینیک");
        Breadcrumbs.Add("باشگاه مشتریان");

        UseLoadStatus(ReloadAsync);
        RefreshOnDataChange(changes, () => _ = ReloadAsync(),
            DataDomain.Sales, DataDomain.Cashier, DataDomain.Finance, DataDomain.Patients, DataDomain.Visits);
    }

    /// <summary>False when the viewer lacks <c>reports.read</c>: the leaderboards and their KPIs are hidden.</summary>
    public bool CanSeeFinancials { get; }

    public RangeObservableCollection<BirthdayRow> Birthdays { get; } = [];

    public RangeObservableCollection<TopClientRow> TopSpenders { get; } = [];

    public RangeObservableCollection<TopClientRow> TopVisitors { get; } = [];

    [ObservableProperty]
    private bool _hasBirthdays;

    [ObservableProperty]
    private bool _hasTopSpenders;

    [ObservableProperty]
    private bool _hasTopVisitors;

    /// <summary>True once the first successful load is on screen (keeps the page body hidden behind the loading bar before).</summary>
    [ObservableProperty]
    private bool _hasLoaded;

    /// <summary>UI-only for now (not persisted, no backend): the «ارسال خودکار پیامک تولد» switch. TODO(backend): wire SMS gateway + setting.</summary>
    [ObservableProperty]
    private bool _autoBirthdaySms;

    [ObservableProperty]
    private string _birthdaysTodayCount = "۰";

    [ObservableProperty]
    private string _birthdaysFoot = "امروز تولدی ثبت نشده است";

    [ObservableProperty]
    private string _topSpenderName = "—";

    [ObservableProperty]
    private string _topSpenderFoot = string.Empty;

    [ObservableProperty]
    private string _topVisitorName = "—";

    [ObservableProperty]
    private string _topVisitorFoot = string.Empty;

    [ObservableProperty]
    private string _topSpendTotal = "—";

    [ObservableProperty]
    private string _topSpendTotalFoot = string.Empty;

    // Singleton page: re-check on every visit; the shell's load-status bar shows progress / failure.
    public override void OnNavigatedTo()
    {
        if (!IsLoadInFlight) _ = ReloadAsync();
    }

    // Leaving the page cancels an in-flight load and frees the per-visit lists; the next visit re-queries.
    public override void OnNavigatedFrom()
    {
        CancelPending();
        Birthdays.Clear();
        TopSpenders.Clear();
        TopVisitors.Clear();
        HasLoaded = false;
    }

    /// <summary>UI-only: the real gateway is not wired yet, so no backend is called.</summary>
    [RelayCommand]
    private void SendBirthdaySms(BirthdayRow? row)
    {
        if (row is null) return;
        _toast.Show("ارسال پیامک تبریک به‌زودی فعال می‌شود.", Tone.Info);
    }

    [RelayCommand]
    private Task RetryAsync() => ReloadAsync();

    private Task ReloadAsync() => TrackLoadResultAsync(LoadAsync);

    private async Task<bool> LoadAsync()
    {
        CancelPending();
        var cts = _loadCts = new CancellationTokenSource();
        var token = ++_generation;
        var ct = cts.Token;
        try
        {
            await _readiness.WaitAsync(ct);
            var birthdaysTask = _service.GetTodaysBirthdaysAsync(ct);
            var spendersTask = CanSeeFinancials ? _service.GetTopSpendersAsync(LeaderboardSize, ct) : Task.FromResult<IReadOnlyList<TopClient>>([]);
            var visitorsTask = CanSeeFinancials ? _service.GetTopVisitorsAsync(LeaderboardSize, ct) : Task.FromResult<IReadOnlyList<TopClient>>([]);
            await Task.WhenAll(birthdaysTask, spendersTask, visitorsTask);

            // A newer reload superseded this one: its result owns the page state.
            if (token != _generation) return true;
            var birthdays = birthdaysTask.Result;
            var spenders = spendersTask.Result;
            var visitors = visitorsTask.Result;
            await PublishOnUiAsync(() => Publish(birthdays, spenders, visitors));
            return true;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        catch (StatisticsQueryException ex)
        {
            return Fail(ex.UserMessage, token);
        }
        catch (Exception)
        {
            return Fail("بارگذاری باشگاه مشتریان انجام نشد؛ دوباره تلاش کنید.", token);
        }
    }

    // Previous data stays on screen; the shell's status bar offers the retry, the toast names the cause.
    private bool Fail(string message, int token)
    {
        if (token != _generation) return true;
        _toast.Show(message, Tone.Danger);
        return false;
    }

    private void Publish(IReadOnlyList<BirthdayPet> birthdays, IReadOnlyList<TopClient> spenders, IReadOnlyList<TopClient> visitors)
    {
        Birthdays.ReplaceAll(birthdays.Select(ToRow));
        HasBirthdays = birthdays.Count > 0;
        BirthdaysTodayCount = StatisticsFormat.Count(birthdays.Count);
        BirthdaysFoot = birthdays.Count == 0 ? "امروز تولدی ثبت نشده است" : "حیوان خانگی امروز تولد دارد";

        var spendMax = spenders.Count == 0 ? 0 : spenders.Max(c => c.TotalSpendMinor);
        TopSpenders.ReplaceAll(spenders.Select((c, i) => ToRow(c, i, StatisticsFormat.Money(c.TotalSpendMinor),
            $"{StatisticsFormat.Count(c.InvoiceCount)} فاکتور", spendMax <= 0 ? 0 : c.TotalSpendMinor / (double)spendMax)));
        HasTopSpenders = spenders.Count > 0;

        var visitMax = visitors.Count == 0 ? 0 : visitors.Max(c => c.InvoiceCount);
        TopVisitors.ReplaceAll(visitors.Select((c, i) => ToRow(c, i, $"{StatisticsFormat.Count(c.InvoiceCount)} مراجعه",
            StatisticsFormat.Money(c.TotalSpendMinor), visitMax <= 0 ? 0 : c.InvoiceCount / (double)visitMax)));
        HasTopVisitors = visitors.Count > 0;

        var topSpender = spenders.FirstOrDefault();
        TopSpenderName = topSpender?.FullName ?? "—";
        TopSpenderFoot = topSpender is null ? "هنوز خریدی ثبت نشده است" : StatisticsFormat.Money(topSpender.TotalSpendMinor);
        var topVisitor = visitors.FirstOrDefault();
        TopVisitorName = topVisitor?.FullName ?? "—";
        TopVisitorFoot = topVisitor is null ? "هنوز مراجعه‌ای ثبت نشده است" : $"{StatisticsFormat.Count(topVisitor.InvoiceCount)} مراجعه";
        var total = spenders.Sum(c => c.TotalSpendMinor);
        TopSpendTotal = spenders.Count == 0 ? "—" : StatisticsFormat.Money(total);
        TopSpendTotalFoot = spenders.Count == 0 ? string.Empty : $"مجموع خرید {StatisticsFormat.Count(spenders.Count)} مشتری برتر";
        HasLoaded = true;
    }

    private static BirthdayRow ToRow(BirthdayPet b)
    {
        var look = SpeciesPresentation.For(b.Species, b.Species);
        return new BirthdayRow(b.PetName, look.Name, look.IconKey, b.OwnerName, PhoneText(b.Phone), AgeText(b.AgeYears),
            b.PetId, b.OwnerId);
    }

    private static TopClientRow ToRow(TopClient c, int index, string primary, string secondary, double fraction) =>
        new(StatisticsFormat.Count(index + 1), c.FullName, PhoneText(c.Phone), primary, secondary,
            c.LastInvoiceUtc is { } last ? StatisticsFormat.JalaliShort(last) : "—", fraction);

    private static string PhoneText(string? phone) =>
        string.IsNullOrWhiteSpace(phone) ? "—" : PersianNumberFormatter.ToPersianDigits(phone);

    private static string AgeText(int years) =>
        years <= 0 ? "تازه متولد شده" : $"{StatisticsFormat.Count(years)} ساله";

    private static Task PublishOnUiAsync(Action publish)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            publish();
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(publish).GetTask();
    }

    private void CancelPending()
    {
        var cts = _loadCts;
        _loadCts = null;
        if (cts is null) return;
        cts.Cancel();
        cts.Dispose();
    }
}
