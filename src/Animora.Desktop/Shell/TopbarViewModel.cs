using System;
using System.Linq;
using System.Threading.Tasks;
using Animora.Application.Ports;
using Animora.Contracts.Identity;
using Animora.Contracts.Sync;
using Animora.Desktop.Services;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Desktop.Shell;

/// <summary>
/// Drives the topbar: the page title/breadcrumb block (tracked from the
/// navigation service's current page), the command-palette search trigger, the
/// sync-status box (backed by <see cref="ISyncStatusService"/>), an inert
/// notification bell, and the user chip bound to <see cref="ICurrentUserContext"/>.
/// </summary>
/// <remarks>
/// B04c: while the capability gate reports no permitted write capability (offline / no snapshot), the topbar's
/// EXISTING status box is reused as a read-only explanation — the no-XAML banner equivalent. When the gate
/// recovers, the normal sync presentation is restored.
/// </remarks>
public sealed partial class TopbarViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly IToastService _toast;
    private readonly ISyncStatusService _sync;
    private readonly IDialogService _dialogs;
    private readonly IServiceProvider _services;
    private readonly ICapabilityGate _gate;

    /// <summary>
    /// Resolves the clinic timezone so "last synced" is rendered in the clinic's local time, not the device's —
    /// a device in another zone (or UTC in a sandbox) must still show the clinic's correct relative time. This
    /// is the root-cause fix for the topbar previously showing a wrong last-sync time.
    /// </summary>
    private readonly IClinicTimeZoneProvider _clinicZone;

    /// <summary>Refreshes the relative "N دقیقه پیش" text every minute while idle.</summary>
    private readonly DispatcherTimer _relativeTicker;

    [ObservableProperty]
    private PageViewModelBase? _currentPage;

    [ObservableProperty]
    private bool _isSidebarOpen = true;

    /// <summary>
    /// True while the gate permits no write capability (no warm snapshot / offline / all writes denied): the shell
    /// runs read-only. Derived from the gate, never from a UI role.
    /// </summary>
    [ObservableProperty]
    private bool _isReadOnlyMode;

    // Chevron rotation: 0° = arrow pointing toward the sidebar (collapse hint)
    // while expanded; 180° flips it to point away (expand hint) while collapsed.
    // The view tweens between the two angles for a smooth direction flip.
    [ObservableProperty]
    private double _toggleIconRotation;

    [ObservableProperty]
    private string _sidebarToggleToolTip = "جمع کردن منو";

    /// <summary>Primary sync line (e.g. «همگام‌سازی شد»).</summary>
    [ObservableProperty]
    private string _syncStatusText = "همگام‌سازی شد";

    /// <summary>Muted relative-time line (e.g. «هم‌اکنون», «۳ دقیقه پیش»).</summary>
    [ObservableProperty]
    private string _syncRelativeText = "هم‌اکنون";

    /// <summary>Dot colour for the current <see cref="SyncState"/>.</summary>
    [ObservableProperty]
    private IBrush _syncStateBrush = Brushes.Transparent;

    /// <summary>
    /// Fill of the whole sync button, tinted to draw attention when there are unsent changes (Pending) or a
    /// failure (Error / Offline). Neutral (the default surface) while synced/idle.
    /// </summary>
    [ObservableProperty]
    private IBrush _syncBoxBackground = Controls.BrushCache.Solid("#f2f4f7");

    /// <summary>Border of the sync button, matched to <see cref="SyncBoxBackground"/>.</summary>
    [ObservableProperty]
    private IBrush _syncBoxBorder = Controls.BrushCache.Solid("#e4e7ec");

    /// <summary>True while a sync pass is running (drives the spinner).</summary>
    [ObservableProperty]
    private bool _isSyncing;

    /// <summary>Fuller description shown on hover.</summary>
    [ObservableProperty]
    private string _syncToolTip = "کلیک برای همگام‌سازی دستی";

    public TopbarViewModel(
        INavigationService navigation,
        IToastService toast,
        ISyncStatusService sync,
        IDialogService dialogs,
        IServiceProvider services,
        ICurrentUserContext currentUser,
        ICapabilityGate gate,
        IClinicTimeZoneProvider clinicZone)
    {
        _navigation = navigation;
        _toast = toast;
        _sync = sync;
        _dialogs = dialogs;
        _services = services;
        _gate = gate;
        _clinicZone = clinicZone;
        CurrentUser = currentUser;

        _navigation.CurrentViewModelChanged += OnCurrentViewModelChanged;
        CurrentPage = _navigation.CurrentViewModel;

        _sync.StateChanged += OnSyncStateChanged;
        UpdateReadOnlyMode();
        RefreshSyncPresentation();

        _gate.CapabilitiesChanged += OnCapabilitiesChanged;

        // Cheap 60s ticker only to keep the relative caption fresh; no work is
        // done when the shell isn't visible (DispatcherTimer is UI-thread bound).
        _relativeTicker = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _relativeTicker.Tick += (_, _) => UpdateSyncRelativeText();
        _relativeTicker.Start();
    }

    public ICurrentUserContext CurrentUser { get; }

    private void OnCurrentViewModelChanged(object? sender, EventArgs e) =>
        CurrentPage = _navigation.CurrentViewModel;

    /// <summary>
    /// Open the Ctrl+K command palette. Presented through <see cref="IDialogService"/>
    /// so it reuses the shell's modal overlay host (backdrop + z-order). The
    /// palette ViewModel is resolved fresh from DI so every open starts clean.
    /// </summary>
    [RelayCommand]
    private void OpenSearch()
    {
        var palette = _services.GetRequiredService<Search.CommandPaletteViewModel>();
        _dialogs.ShowModal(palette, maxWidth: 660);
    }

    [RelayCommand]
    private void OpenNotifications() =>
        _toast.Show("۵ اعلان تازه دارید.");

    /// <summary>
    /// Raised when the fold button is pressed. The shell (MainWindowViewModel)
    /// listens to this to collapse/expand the sidebar, keeping the toggle state
    /// out of the topbar itself.
    /// </summary>
    public event EventHandler? SidebarToggleRequested;

    [RelayCommand]
    private void ToggleSidebar() =>
        // The shell owns the open/closed state and pushes it back via
        // SyncSidebarState, so both the topbar and in-sidebar toggles stay
        // consistent regardless of which one was pressed.
        SidebarToggleRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Push the authoritative open/closed state (owned by the shell) into the
    /// topbar's fold control so its glyph/tooltip always reflect reality.
    /// </summary>
    public void SyncSidebarState(bool isOpen)
    {
        IsSidebarOpen = isOpen;
        ToggleIconRotation = isOpen ? 0 : 180;
        SidebarToggleToolTip = isOpen ? "جمع کردن منو" : "باز کردن منو";
    }

    [RelayCommand]
    private void ToggleTheme() =>
        _toast.Show("تم تیره در این نسخه فعال نیست.");

    // ---- Sync status box -----------------------------------------------------

    /// <summary>
    /// Manual sync trigger. A single pass BOTH flushes any queued local changes AND pulls server state, so one
    /// click "checks whether there is anything to upload/download and syncs it if so" — exactly what is needed
    /// after the app has been offline for a while. The toast afterwards reflects what actually happened
    /// (uploaded N changes / already up to date / still offline / error) instead of always claiming success.
    /// </summary>
    [RelayCommand]
    private async Task SyncNowAsync()
    {
        var pendingBefore = _sync.PendingChangeCount;
        Diagnostics.AppTrace.Info(
            $"[topbar] SyncNow CLICKED — before: state={_sync.State} pending={pendingBefore} terminal={_sync.TerminalFailureCount} " +
            $"readOnly={IsReadOnlyMode} gate={DescribeGate()}", "topbar");
        await _sync.SyncNowAsync();

        var message = _sync.State switch
        {
            SyncState.Synced when pendingBefore > 0 =>
                $"{Localization.PersianNumberFormatter.ToPersianDigits(pendingBefore)} تغییر ارسال و همگام‌سازی شد",
            SyncState.Synced => "همه‌چیز به‌روز است",
            SyncState.NeedsAttention =>
                $"{Localization.PersianNumberFormatter.ToPersianDigits(_sync.TerminalFailureCount)} تغییر توسط سرور رد شد و نیاز به بررسی دارد",
            SyncState.Pending =>
                $"{Localization.PersianNumberFormatter.ToPersianDigits(_sync.PendingChangeCount)} تغییر در انتظار ارسال باقی ماند",
            SyncState.Offline => "آفلاین: اتصال به سرور برقرار نشد؛ تغییرات پس از اتصال ارسال می‌شود",
            SyncState.Error => "خطا در همگام‌سازی؛ بعداً دوباره تلاش می‌شود",
            _ => "همگام‌سازی انجام شد",
        };
        Diagnostics.AppTrace.Info(
            $"[topbar] SyncNow RETURNED — after: state={_sync.State} pending={_sync.PendingChangeCount} terminal={_sync.TerminalFailureCount} " +
            $"readOnly={IsReadOnlyMode} toast='{message}'. NOTE: a manual sync NEVER re-warms the capability gate — if the shell is read-only " +
            "because the gate is empty, this click cannot clear it (only a successful LiveAccessRefresh/mirror can).", "topbar");
        _toast.Show(message);
    }

    private void OnSyncStateChanged(object? sender, EventArgs e)
    {
        Diagnostics.AppTrace.Debug($"[topbar] OnSyncStateChanged → state={_sync.State} pending={_sync.PendingChangeCount} readOnly={IsReadOnlyMode}.", "topbar");
        // Raised from a background thread by the (future) engine — marshal the
        // property updates back to the UI thread.
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshSyncPresentation();
        }
        else
        {
            Dispatcher.UIThread.Post(RefreshSyncPresentation);
        }
    }

    private void OnCapabilitiesChanged(object? sender, EventArgs e)
    {
        // The gate may raise this from a background thread; marshal to the UI thread.
        var wasReadOnly = IsReadOnlyMode;
        void Apply()
        {
            UpdateReadOnlyMode();
            RefreshSyncPresentation();
            Diagnostics.AppTrace.Info(
                $"[topbar] OnCapabilitiesChanged — readOnly {wasReadOnly}→{IsReadOnlyMode}; gate={DescribeGate()}.", "topbar");
        }
        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }

    /// <summary>
    /// Read-only when the gate permits no capability at all (no warm snapshot / signed out). Otherwise the shell
    /// still has an authority snapshot, so a single unrelated denial is not treated as read-only.
    /// </summary>
    private void UpdateReadOnlyMode() =>
        IsReadOnlyMode = !NavPermissions.Codes.Any(_gate.Can);

    private void RefreshSyncPresentation()
    {
        if (IsReadOnlyMode)
        {
            // No permitted capability: reuse the existing status box as a read-only banner.
            IsSyncing = false;
            SyncStatusText = "حالت فقط‌خواندنی";
            SyncRelativeText = "دسترسی به داده‌های آفلاین در دسترس نیست";
            SyncToolTip = "بدون دسترسی آفلاین: تغییرات غیرفعال است تا اتصال و نشست دوباره برقرار شود.";
            SyncStateBrush = Controls.BrushCache.Solid("#b54708");
            ApplyBoxTint(SyncState.Pending); // amber banner surface
            Diagnostics.AppTrace.Warn(
                $"[topbar] READ-ONLY banner ACTIVE — no nav capability is permitted. gate={DescribeGate()} syncState={_sync.State} " +
                $"pending={_sync.PendingChangeCount} terminal={_sync.TerminalFailureCount} lastSynced={_sync.LastSyncedAt?.ToString("O") ?? "(none)"}. " +
                "This is the state where the sidebar + content are expected to be empty.", "topbar");
            return;
        }

        var pending = _sync.PendingChangeCount;
        IsSyncing = _sync.State == SyncState.Syncing;
        SyncStatusText = _sync.State switch
        {
            SyncState.Syncing => "در حال همگام‌سازی…",
            SyncState.Synced => "همگام‌سازی شد",
            // Surface the backlog size so the user knows there is unsent work (offline-for-a-while case).
            SyncState.Pending when pending > 0 =>
                $"{Localization.PersianNumberFormatter.ToPersianDigits(pending)} تغییر ارسال‌نشده",
            SyncState.Pending => "در انتظار همگام‌سازی",
            SyncState.Error => "خطا در همگام‌سازی",
            SyncState.NeedsAttention =>
                $"{Localization.PersianNumberFormatter.ToPersianDigits(_sync.TerminalFailureCount)} تغییر نیازمند بررسی",
            SyncState.Offline => "آفلاین",
            _ => "همگام‌سازی",
        };
        SyncStateBrush = _sync.State switch
        {
            SyncState.Synced => Controls.BrushCache.Solid("#027a48"),
            SyncState.Pending => Controls.BrushCache.Solid("#b54708"),
            SyncState.Error or SyncState.Offline or SyncState.NeedsAttention => Controls.BrushCache.Solid("#d92d20"),
            SyncState.Syncing => Controls.BrushCache.Solid("#1570cd"),
            _ => Controls.BrushCache.Solid("#98a2b3"),
        };
        ApplyBoxTint(_sync.State);
        UpdateSyncRelativeText();
    }

    /// <summary>Formats the capability gate's current authority for the read-only diagnosis (best-effort).</summary>
    private string DescribeGate() =>
        _gate is Animora.Client.Services.Identity.CapabilityGateService concrete
            ? concrete.DescribeState()
            : "(gate is not the concrete CapabilityGateService)";

    /// <summary>
    /// Tints the whole sync button so unsent changes / failures are noticeable at a glance:
    /// amber for Pending (changes queued), red for Error/Offline, neutral otherwise.
    /// </summary>
    private void ApplyBoxTint(SyncState state)
    {
        (SyncBoxBackground, SyncBoxBorder) = state switch
        {
            SyncState.Pending => (Controls.BrushCache.Solid("#fffaeb"), Controls.BrushCache.Solid("#fec84b")),
            SyncState.Error or SyncState.Offline or SyncState.NeedsAttention => (Controls.BrushCache.Solid("#fef3f2"), Controls.BrushCache.Solid("#fda29b")),
            _ => (Controls.BrushCache.Solid("#f2f4f7"), Controls.BrushCache.Solid("#e4e7ec")),
        };
    }

    private void UpdateSyncRelativeText()
    {
        if (IsReadOnlyMode)
        {
            return; // the read-only banner owns the status text while it is active.
        }

        // Render the UTC watermark in the CLINIC timezone, not the device's local zone, so a device set to a
        // different zone (or UTC in a sandbox) still shows the clinic's correct "last synced" time. This fixes the
        // topbar previously showing a wrong time whenever the OS zone ≠ clinic zone.
        var zone = _clinicZone.GetClinicTimeZone();
        var when = _sync.LastSyncedAt is { } utc
            ? TimeZoneInfo.ConvertTime(utc, zone).DateTime
            : (DateTime?)null;
        SyncRelativeText = when is { } value ? FormatRelative(value, zone) : "—";

        SyncToolTip = when is { } stamp
            ? $"آخرین همگام‌سازی: {stamp:HH:mm} — کلیک برای همگام‌سازی دستی"
            : "کلیک برای همگام‌سازی دستی";
    }

    // Buckets per the task spec: <1min «هم‌اکنون», <60min «N دقیقه پیش»,
    // <24h «N ساعت پیش», else the Jalali date. "now" is the clinic's own now
    // (UTC projected into the clinic zone) so the delta matches the rendered stamp.
    private static string FormatRelative(DateTime when, TimeZoneInfo zone)
    {
        var nowClinic = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime;
        var delta = nowClinic - when;
        if (delta < TimeSpan.FromMinutes(1))
        {
            return "هم‌اکنون";
        }

        if (delta < TimeSpan.FromHours(1))
        {
            var minutes = (int)delta.TotalMinutes;
            return $"{Localization.PersianNumberFormatter.ToPersianDigits(minutes)} دقیقه پیش";
        }

        if (delta < TimeSpan.FromHours(24))
        {
            var hours = (int)delta.TotalHours;
            return $"{Localization.PersianNumberFormatter.ToPersianDigits(hours)} ساعت پیش";
        }

        return Localization.JalaliDate.FromDateTime(when).ToString("short");
    }
}
