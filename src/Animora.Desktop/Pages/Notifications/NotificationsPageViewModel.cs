using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Animora.Application.Ports;
using Animora.Contracts.Diagnostics;
using Animora.Contracts.Notifications;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Notifications;

/// <summary>
/// «اعلان‌ها و پیام‌ها»: the signed-in user's message feed (newest first) with read/unread + kind filters, mark-read on
/// open and «خواندن همه». It is a strictly read-only receiver: messages and rewards are authored by the vendor in the
/// admin panel and arrive via sync, so there is no compose / grant / reply action here. Everything goes through
/// <see cref="INotificationService"/>. The feed refreshes at once on <see cref="DataDomain.Notifications"/>.
/// </summary>
public sealed partial class NotificationsPageViewModel : PageViewModelBase
{
    private readonly INotificationService _service;
    private readonly IStoreReadinessGate _readiness;
    private readonly IToastService _toast;
    private readonly IClinicTimeZoneProvider _zones;
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private List<NotificationItemViewModel> _all = [];
    private string _scope = "all";
    private string _kind = "all";
    private int _generation;

    public NotificationsPageViewModel(INotificationService service, IStoreReadinessGate readiness, IToastService toast,
        IDataChangeNotifier changes, IClinicTimeZoneProvider zones)
    {
        _service = service;
        _readiness = readiness;
        _toast = toast;
        _zones = zones;

        Title = "اعلان‌ها و پیام‌ها";
        Eyebrow = "پیام‌های کلینیک";
        Breadcrumbs.Add("سیستم");
        Breadcrumbs.Add("اعلان‌ها و پیام‌ها");

        ScopeChips.AddRange([new("all", "همه"), new("unread", "خوانده‌نشده")]);
        KindChips.AddRange([new("all", "همهٔ انواع")]);
        foreach (var kind in new[]
                 {
                     NotificationKind.Info, NotificationKind.Warning, NotificationKind.Success,
                     NotificationKind.Promo, NotificationKind.System, NotificationKind.Reward,
                 })
            KindChips.Add(new(kind.ToString(), NotificationsPresentation.KindLabel(kind)));
        MarkChips();

        UseLoadStatus(ReloadAsync);
        RefreshOnDataChange(changes, () => _ = ReloadAsync(), DataDomain.Notifications);
    }

    public RangeObservableCollection<NotificationChipViewModel> ScopeChips { get; } = [];

    public RangeObservableCollection<NotificationChipViewModel> KindChips { get; } = [];

    public RangeObservableCollection<NotificationItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private int _unreadCount;

    [ObservableProperty]
    private bool _hasItems;

    [ObservableProperty]
    private string _emptyTitle = "پیامی برای نمایش وجود ندارد";

    [ObservableProperty]
    private string _emptySubtitle = "پیام‌های تازه به‌محض رسیدن اینجا نمایش داده می‌شوند.";

    public bool HasUnread => UnreadCount > 0;

    public string UnreadText => UnreadCount > 0
        ? $"{PersianNumberFormatter.ToPersianDigits(UnreadCount)} پیام خوانده‌نشده"
        : "همهٔ پیام‌ها خوانده شده‌اند";

    partial void OnUnreadCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnread));
        OnPropertyChanged(nameof(UnreadText));
    }

    public override void OnNavigatedTo()
    {
        if (!IsLoadInFlight) _ = ReloadAsync();
    }

    // F-05 style: a hidden singleton page drops its cards; OnNavigatedTo re-queries.
    public override void OnNavigatedFrom()
    {
        Items.Clear();
        _all = [];
        HasItems = false;
    }

    private Task ReloadAsync() => TrackLoadAsync(LoadAsync);

    private async Task LoadAsync()
    {
        await _readiness.WaitAsync();
        var token = ++_generation;
        var feed = await _service.GetFeedAsync();
        if (token != _generation) return; // a newer load superseded this one

        var now = DateTimeOffset.UtcNow;
        var zone = _zones.GetClinicTimeZone();
        _all = feed.Items.Select(d => new NotificationItemViewModel(d, now, zone, _expanded.Contains(d.Id))).ToList();
        UnreadCount = feed.UnreadCount;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        IEnumerable<NotificationItemViewModel> query = _all;
        if (_scope == "unread") query = query.Where(i => i.IsUnread);
        if (_kind != "all") query = query.Where(i => i.Kind.ToString() == _kind);
        Items.ReplaceAll(query);
        HasItems = Items.Count > 0;
        if (_all.Count == 0)
        {
            EmptyTitle = "پیامی برای نمایش وجود ندارد";
            EmptySubtitle = "پیام‌های تازه به‌محض رسیدن اینجا نمایش داده می‌شوند.";
        }
        else
        {
            EmptyTitle = "پیامی با این فیلتر پیدا نشد";
            EmptySubtitle = "فیلتر «همه» یا «همهٔ انواع» را انتخاب کنید.";
        }
    }

    private void MarkChips()
    {
        foreach (var chip in ScopeChips) chip.IsActive = chip.Key == _scope;
        foreach (var chip in KindChips) chip.IsActive = chip.Key == _kind;
    }

    [RelayCommand]
    private void SetScope(string? key)
    {
        _scope = key == "unread" ? "unread" : "all";
        MarkChips();
        ApplyFilter();
    }

    [RelayCommand]
    private void SetKind(string? key)
    {
        _kind = string.IsNullOrEmpty(key) ? "all" : key;
        MarkChips();
        ApplyFilter();
    }

    /// <summary>Opening a card toggles its full text and marks it read (the unread badge drops by one).</summary>
    [RelayCommand]
    private async Task OpenItemAsync(NotificationItemViewModel? item)
    {
        if (item is null) return;
        item.IsExpanded = !item.IsExpanded;
        if (item.IsExpanded) _expanded.Add(item.Id);
        else _expanded.Remove(item.Id);
        if (item.IsRead) return;

        item.IsRead = true;
        UnreadCount = Math.Max(0, UnreadCount - 1);
        try
        {
            await _service.MarkReadAsync(item.Id);
        }
        catch (InvalidOperationException ex)
        {
            item.IsRead = false;
            UnreadCount++;
            _toast.Show(NotificationsPresentation.Failure(ex, "ثبت وضعیت خوانده‌شدن ناموفق بود؛ دوباره تلاش کنید."), Tone.Danger);
        }
    }

    [RelayCommand]
    private async Task MarkAllReadAsync()
    {
        if (UnreadCount == 0) return;
        try
        {
            await _service.MarkAllReadAsync();
            _toast.Show("همهٔ پیام‌ها خوانده شد", Tone.Ok);
        }
        catch (InvalidOperationException ex)
        {
            _toast.Show(NotificationsPresentation.Failure(ex, "ثبت وضعیت خوانده‌شدن ناموفق بود؛ دوباره تلاش کنید."), Tone.Danger);
        }
    }
}
