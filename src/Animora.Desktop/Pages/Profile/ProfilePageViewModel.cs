using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Diagnostics;
using Animora.Contracts.Profile;
using Animora.Desktop.Controls;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Profile;

/// <summary>Settings workspace. All persistence and authorization go through Contracts services.</summary>
public sealed partial class ProfilePageViewModel : PageViewModelBase
{
    private readonly IUserProfileService _service;
    private readonly IStoreReadinessGate _readiness;
    private readonly IToastService _toast;

    public ProfilePageViewModel(IUserProfileService service, IStoreReadinessGate readiness, IToastService toast)
    {
        _service = service;
        _readiness = readiness;
        _toast = toast;

        Title = "تنظیمات";
        Eyebrow = "حساب کاربری";
        Breadcrumbs.Add("حساب کاربری");
        Breadcrumbs.Add("پروفایل و تنظیمات");

        foreach (var (id, label, iconKey) in TabCatalog)
        {
            Tabs.Add(new ProfileTabViewModel(id, label, iconKey) { IsActive = id == ActiveTab });
        }

        UseLoadStatus(ReloadPageAsync);
    }

    // ── Chrome ───────────────────────────────────────────────────────────

    public string PageEyebrow => "حساب کاربری";

    public string PageTitle => "تنظیمات کلینیک";

    public string PageSubtitle =>
        "اشتراک، تیم و ترجیحات کاری؛ همه در یک فضای مشخص و منظم.";

    // ── Hero identity ────────────────────────────────────────────────────

    [ObservableProperty]
    private string _userName = string.Empty;

    [ObservableProperty]
    private string _userRole = string.Empty;

    [ObservableProperty]
    private string _userInitials = string.Empty;

    [ObservableProperty]
    private string _userRoleLine = string.Empty;

    [ObservableProperty]
    private string _userPhone = string.Empty;

    [ObservableProperty]
    private string _userEmail = string.Empty;

    [ObservableProperty]
    private string _lastLoginLine = string.Empty;

    public RangeObservableCollection<ProfileKpiViewModel> Kpis { get; } = [];

    // ── Sub-nav rail + panels ────────────────────────────────────────────

    public ObservableCollection<ProfileTabViewModel> Tabs { get; } = [];

    [ObservableProperty]
    private string _activeTab = "overview";

    public bool IsOverviewTab => ActiveTab == "overview";
    public bool IsPreferencesTab => IsScheduleTab || IsAlertsTab || IsLookTab;
    public bool IsEditableTab => IsProfileTab || IsPreferencesTab;
    public string ActiveSectionTitle => Tabs.FirstOrDefault(t => t.Id == ActiveTab)?.Label ?? "تنظیمات";
    public string ActiveSectionDescription => ActiveTab switch
    {
        "overview" => "وضعیت خدمات و دسترسی سریع به تنظیمات پرکاربرد",
        "profile" => "اطلاعات تماس حساب فعلی و اعضای تیم کلینیک",
        "sched" => "ساعات کاری، بازه‌های نوبت و ترجیحات نمایش تقویم",
        "alerts" => "کانال‌های اطلاع‌رسانی و زمان‌های سکوت",
        "look" => "ترجیحات نمایش و خوانایی محیط کار",
        "sec" => "وضعیت امنیت حساب و دستگاه‌های واردشده",
        "access" => "ثبت حساب واقعی، نقش‌های سازمانی و مجوز هر عملیات",
        _ => string.Empty,
    };
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _hasSettingsChanges;
    [ObservableProperty] private bool _hasProfileChanges;
    [ObservableProperty] private string _saveMessage = "";
    [ObservableProperty] private string _newMemberError = "";
    public bool HasNewMemberError => !string.IsNullOrEmpty(NewMemberError);
    partial void OnNewMemberErrorChanged(string value) => OnPropertyChanged(nameof(HasNewMemberError));
    public string SaveButtonLabel => IsProfileTab ? "ذخیره اطلاعات تماس" : "ذخیره ترجیحات";
    public string ChangesHint => (IsProfileTab ? HasProfileChanges : HasSettingsChanges)
        ? "تغییرات ذخیره‌نشده دارید" : "تغییری برای ذخیره وجود ندارد";
    partial void OnHasSettingsChangesChanged(bool value) => NotifySaveState();
    partial void OnHasProfileChangesChanged(bool value) => NotifySaveState();
    partial void OnIsSavingChanged(bool value) => NotifySaveState();
    partial void OnProfilePhoneChanged(string value) { if (!IsBusy) HasProfileChanges = true; }
    partial void OnProfileEmailChanged(string value) { if (!IsBusy) HasProfileChanges = true; }
    private void NotifySaveState()
    {
        OnPropertyChanged(nameof(ChangesHint));
        SaveCurrentChangesCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty] private string _subscriptionRemaining = "در حال دریافت…";
    [ObservableProperty] private string _subscriptionDetail = "";
    [ObservableProperty] private string _subscriptionPlan = "اشتراک نرم‌افزار";
    [ObservableProperty] private string _smsCredit = "در حال دریافت…";
    [ObservableProperty] private string _smsDetail = "";
    [ObservableProperty] private string _accountStatusMessage = "";

    private static string Remaining(System.DateTimeOffset? expires, System.DateTimeOffset now) => expires is null
        ? "اطلاعات در دسترس نیست"
        : expires <= now ? "منقضی شده" : $"{ProfilePresentation.Fa(System.Math.Ceiling((expires.Value - now).TotalDays).ToString("0"))} روز باقی‌مانده";

    [RelayCommand]
    private async Task RefreshAccountStatusAsync()
    {
        try
        {
            var status = await _service.GetAccountStatusAsync();
            var now = System.DateTimeOffset.UtcNow;
            SubscriptionPlan = status.PlanName ?? "اشتراک نرم‌افزار";
            SubscriptionRemaining = Remaining(status.SubscriptionExpiresAtUtc, now);
            SubscriptionDetail = status.SubscriptionExpiresAtUtc is null
                ? "پس از اتصال به سرویس اشتراک، زمان باقی‌مانده نمایش داده می‌شود."
                : "اعتبار پلن فعلی نرم‌افزار";
            SmsCredit = status.SmsCredit is { } credit
                ? $"{ProfilePresentation.Fa(credit.ToString("N0"))} {status.SmsCreditUnit}"
                : status.SmsRemainingMessages is { } count ? $"{ProfilePresentation.Fa(count.ToString("N0"))} پیامک" : "اطلاعات در دسترس نیست";
            SmsDetail = status.SmsExpiresAtUtc is { } expiry
                ? $"اعتبار پنل: {Remaining(expiry, now)}"
                : "مانده اعتبار و زمان اشتراک از سرویس پنل پیامک دریافت می‌شود.";
            AccountStatusMessage = status.ObservedAtUtc is { } observed
                ? $"{(status.IsStale ? "اطلاعات ذخیره‌شده؛ نیازمند به‌روزرسانی" : "آخرین دریافت اطلاعات")}: {ProfilePresentation.Fa(observed.ToLocalTime().ToString("yyyy/MM/dd HH:mm"))}"
                : "سرویس اشتراک و پنل پیامک هنوز متصل نیستند؛ مقدار نامشخص به معنی اعتبار صفر نیست.";
        }
        catch (System.Exception)
        {
            SubscriptionRemaining = SmsCredit = "دریافت اطلاعات ناموفق بود";
            SubscriptionDetail = SmsDetail = "برای دریافت وضعیت، دوباره تلاش کنید.";
            AccountStatusMessage = "وضعیت خدمات قابل تأیید نیست؛ از بازخوانی استفاده کنید.";
        }
    }

    public bool IsProfileTab => ActiveTab == "profile";

    public bool IsScheduleTab => ActiveTab == "sched";

    public bool IsAlertsTab => ActiveTab == "alerts";

    public bool IsLookTab => ActiveTab == "look";

    public bool IsSecurityTab => ActiveTab == "sec";

    public bool IsAccessTab => ActiveTab == "access";

    // ── Profile tab: personal info + team ────────────────────────────────

    [ObservableProperty]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private string _profilePhone = string.Empty;

    [ObservableProperty]
    private string _profileEmail = string.Empty;

    [ObservableProperty]
    private string _nationalCodeLine = string.Empty;

    public RangeObservableCollection<ProfileTeamMemberViewModel> Team { get; } = [];

    // ── Scheduling tab ───────────────────────────────────────────────────

    [ObservableProperty]
    private ProfileSelectViewModel _workFromOptions = new(["۰۷:۰۰"], "۰۷:۰۰");

    [ObservableProperty]
    private ProfileSelectViewModel _workToOptions = new(["۲۲:۰۰"], "۲۲:۰۰");

    [ObservableProperty]
    private ProfileSelectViewModel _durationOptions = new(["۳۰ دقیقه"], "۳۰ دقیقه");

    [ObservableProperty]
    private ProfileSelectViewModel _gapOptions = new(["۵ دقیقه"], "۵ دقیقه");

    [ObservableProperty]
    private ProfileSelectViewModel _overlapOptions = new(["بدون هم‌پوشانی"], "بدون هم‌پوشانی");

    [ObservableProperty]
    private ProfileSelectViewModel _calendarViewOptions = new(["هفته"], "هفته");

    public ObservableCollection<ProfileDayChipViewModel> DayChips { get; } = [];

    public ObservableCollection<ProfileToggleViewModel> OwnerNotifyToggles { get; } = [];

    public ObservableCollection<ProfileToggleViewModel> CalendarDisplayToggles { get; } = [];

    // ── Alerts tab ───────────────────────────────────────────────────────

    public ObservableCollection<ProfileMatrixRowViewModel> MatrixRows { get; } = [];

    [ObservableProperty]
    private ProfileSelectViewModel _quietFromOptions = new(["۲۲:۰۰"], "۲۲:۰۰");

    [ObservableProperty]
    private ProfileSelectViewModel _quietToOptions = new(["۰۷:۰۰"], "۰۷:۰۰");

    public ObservableCollection<ProfileToggleViewModel> QuietToggles { get; } = [];

    // ── Look tab ─────────────────────────────────────────────────────────

    public ObservableCollection<ProfileThemeOptionViewModel> ThemeOptions { get; } = [];

    public ObservableCollection<ProfileToggleViewModel> LookToggles { get; } = [];

    // ── Security tab ─────────────────────────────────────────────────────

    public ObservableCollection<ProfileToggleViewModel> SecurityToggles { get; } = [];

    public RangeObservableCollection<ProfileSessionViewModel> Sessions { get; } = [];

    // ── Access-management tab (مدیریت دسترسی‌ها) ──────────────────────────

    /// <summary>The persisted clinic staff roster.</summary>
    public ObservableCollection<StaffMemberViewModel> Staff { get; } = [];

    /// <summary>Role-template summary cards (manager / doctor / secretary).</summary>
    public ObservableCollection<StaffRoleCardViewModel> RoleCards { get; } = [];

    /// <summary>Role choices for the «افزودن کاربر» form.</summary>
    [ObservableProperty]
    private ProfileSelectViewModel _newMemberRoleOptions = new([""], "");

    private StaffDirectory? _directory;

    [ObservableProperty] private string _newMemberUsername = string.Empty;
    [ObservableProperty] private string _newMemberPassword = string.Empty;
    [ObservableProperty] private bool _canManageUsers;
    [ObservableProperty] private bool _canManageAccess;
    [ObservableProperty] private bool _isStaffBusy;
    [ObservableProperty] private string _accessMessage = string.Empty;

    public bool CanEditSelected => CanManageAccess && SelectedStaff is { IsProtected: false, IsActive: true } && !IsStaffBusy;
    public bool CanChangeSelectedStatus => CanManageUsers && SelectedStaff is { IsProtected: false } && !IsStaffBusy;
    public string StaffSummary => $"{ProfilePresentation.Fa(Staff.Count.ToString())} کاربر · {ProfilePresentation.Fa(Staff.Count(m => m.IsActive).ToString())} فعال · {ProfilePresentation.Fa(Staff.Count(m => !m.IsActive).ToString())} غیرفعال";
    public string SelectedStatusAction => SelectedStaff?.IsActive == true ? "غیرفعال‌کردن کاربر" : "فعال‌کردن کاربر";
    public string SelectedAccessHint => SelectedStaff is null ? "برای مشاهدهٔ مجوزها، یک کاربر از فهرست انتخاب کنید."
        : SelectedStaff.IsProtected ? "حساب مدیر و حساب فعلی محافظت‌شده‌اند؛ مجوزهای آن‌ها فقط قابل مشاهده است."
        : !SelectedStaff.IsActive ? "کاربر غیرفعال است؛ برای تغییر دسترسی ابتدا حساب را فعال کنید."
        : !CanManageAccess ? "مجوز ویرایش دسترسی‌های کاربران را ندارید."
        : "مجوز مشاهده و انجام عملیات مستقل است. تغییرات فقط با ذخیرهٔ دسترسی‌ها ثبت می‌شوند.";

    partial void OnCanManageUsersChanged(bool value) => NotifyAccessState();
    partial void OnCanManageAccessChanged(bool value) => NotifyAccessState();
    partial void OnIsStaffBusyChanged(bool value) => NotifyAccessState();

    private void NotifyAccessState()
    {
        OnPropertyChanged(nameof(CanEditSelected));
        OnPropertyChanged(nameof(CanChangeSelectedStatus));
        OnPropertyChanged(nameof(SelectedStatusAction));
        OnPropertyChanged(nameof(SelectedAccessHint));
        AddStaffCommand.NotifyCanExecuteChanged();
        RemoveStaffCommand.NotifyCanExecuteChanged();
        SaveAccessCommand.NotifyCanExecuteChanged();
        GrantAllPermissionsCommand.NotifyCanExecuteChanged();
        ResetPermissionsCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty]
    private StaffMemberViewModel? _selectedStaff;

    [ObservableProperty]
    private string _newMemberName = string.Empty;

    [ObservableProperty]
    private string _newMemberPhone = string.Empty;

    partial void OnSelectedStaffChanged(StaffMemberViewModel? value)
    {
        foreach (var member in Staff)
        {
            member.IsSelected = ReferenceEquals(member, value);
        }
        NotifyAccessState();
    }

    // ── Loading ──────────────────────────────────────────────────────────

    // Singleton page: reload on every visit so a staff/account change made elsewhere is never stale.
    public override void OnNavigatedTo()
    {
        // Never overwrite edits the user has not saved yet.
        if (!IsLoadInFlight && !HasProfileChanges && !HasSettingsChanges) _ = ReloadPageAsync();
    }

    private Task ReloadPageAsync() => TrackLoadResultAsync(LoadAsync);

    /// <returns>False when the load failed (already reported to the user here).</returns>
    private async Task<bool> LoadAsync()
    {
        await _readiness.WaitAsync();
        IsBusy = true;

        try
        {
            var profile = await _service.GetAsync();
            var settings = await _service.GetSettingsAsync();

            ApplyProfile(profile);
            ApplySettings(settings);
            await ReloadStaffAsync();
            await RefreshAccountStatusAsync();
            HasSettingsChanges = HasProfileChanges = false;
            return true;
        }
        catch (System.Exception)
        {
            AccessMessage = "دریافت اطلاعات ناموفق بود؛ دوباره تلاش کنید.";
            _toast.Show(AccessMessage, Tone.Warn);
            return false;
        }
        finally
        {
            IsBusy = false;
            NotifySaveState();
        }
    }

    private void ApplyProfile(UserProfile profile)
    {
        UserName = profile.Name;
        UserRole = profile.RoleLabel;
        UserInitials = profile.Initials;
        UserRoleLine = profile.BranchName;
        UserPhone = ProfilePresentation.Fa(profile.Phone);
        UserEmail = profile.Email;
        LastLoginLine = $"آخرین ورود: امروز {ProfilePresentation.Fa(profile.LastLoginTime)}";
        NationalCodeLine = string.IsNullOrEmpty(profile.NationalCode) ? "ثبت نشده" : ProfilePresentation.Fa(profile.NationalCode);

        ProfileName = profile.Name;
        ProfilePhone = ProfilePresentation.Fa(profile.Phone);
        ProfileEmail = profile.Email;

        Kpis.ReplaceAll(profile.Stats.Select(stat =>
            new ProfileKpiViewModel(stat.IconKey, stat.Value, stat.Suffix, stat.Label, stat.Tone)));

        Team.ReplaceAll(profile.Team.Select(member => new ProfileTeamMemberViewModel(
            member.Name, member.Subtitle, member.Initials, member.AccentHex, member.RoleLabel, member.RoleTone)));

        Sessions.ReplaceAll(profile.Sessions.Select(session =>
            new ProfileSessionViewModel(session.Name, session.Detail, session.IsCurrent)));
    }

    private void ApplySettings(AppSettings settings)
    {
        WorkFromOptions = new ProfileSelectViewModel(["۰۶:۰۰", "۰۷:۰۰", "۰۸:۰۰", "۰۹:۰۰"], settings.WorkHoursFrom);
        WorkToOptions = new ProfileSelectViewModel(["۱۸:۰۰", "۲۰:۰۰", "۲۲:۰۰", "۲۳:۰۰"], settings.WorkHoursTo);
        DurationOptions = new ProfileSelectViewModel(["۱۵ دقیقه", "۲۰ دقیقه", "۳۰ دقیقه", "۴۵ دقیقه", "۶۰ دقیقه"], settings.DefaultDuration);
        GapOptions = new ProfileSelectViewModel(["بدون فاصله", "۵ دقیقه", "۱۰ دقیقه", "۱۵ دقیقه"], settings.RestGap);
        OverlapOptions = new ProfileSelectViewModel(["بدون هم‌پوشانی", "۲ نوبت", "۳ نوبت"], settings.OverlapLimit);
        CalendarViewOptions = new ProfileSelectViewModel(["روز", "هفته", "ماه"], settings.DefaultCalendarView);
        QuietFromOptions = new ProfileSelectViewModel(["۲۲:۰۰", "۲۳:۰۰", "۰۰:۰۰"], settings.QuietFrom);
        QuietToOptions = new ProfileSelectViewModel(["۰۶:۰۰", "۰۷:۰۰", "۰۸:۰۰"], settings.QuietTo);

        DayChips.Clear();
        for (var i = 0; i < ProfilePresentation.Weekdays.Count; i++)
        {
            DayChips.Add(new ProfileDayChipViewModel(i, ProfilePresentation.Weekdays[i])
            {
                IsOn = settings.WorkingDays.Contains(i),
            });
        }

        OwnerNotifyToggles.Clear();
        OwnerNotifyToggles.Add(new ProfileToggleViewModel("stRemind", "یادآوری پیامکی", "۲۴ ساعت قبل از نوبت ارسال می‌شود", settings.SmsReminder));
        OwnerNotifyToggles.Add(new ProfileToggleViewModel("stConfirm", "پیامک تأیید فوری", "بلافاصله پس از ثبت یا تأیید نوبت", settings.SmsInstantConfirm));
        OwnerNotifyToggles.Add(new ProfileToggleViewModel("stWaitlist", "اطلاع لغو به لیست انتظار", "پس از لغو، نوبت خالی به متقاضیان اعلان می‌شود", settings.WaitlistNotify));

        CalendarDisplayToggles.Clear();
        CalendarDisplayToggles.Add(new ProfileToggleViewModel("stNow", "نمایش خط «اکنون»", "نشانگر لحظهٔ فعلی روی نمای روز و هفته", settings.ShowNowLine));
        CalendarDisplayToggles.Add(new ProfileToggleViewModel("stCancel", "نمایش نوبت‌های لغوشده", "با حالت کم‌رنگ در تقویم باقی می‌مانند", settings.ShowCancelled));
        CalendarDisplayToggles.Add(new ProfileToggleViewModel("stHint", "راهنمای «نوبت جدید»", "پیش‌نمایش هنگام حرکت نشانگر روی خانه‌های خالی", settings.ShowNewHint));

        MatrixRows.Clear();
        foreach (var (id, label, sub) in MatrixCatalog)
        {
            var setting = settings.NotificationMatrix.FirstOrDefault(m => m.EventId == id);
            MatrixRows.Add(new ProfileMatrixRowViewModel(id, label, sub, setting?.Sms ?? false, setting?.InApp ?? false));
        }

        QuietToggles.Clear();
        QuietToggles.Add(new ProfileToggleViewModel("stQuiet", "فقط اعلان‌های بحرانی", "لغو نوبت امروز و موارد فوری همیشه ارسال می‌شوند", settings.QuietCriticalOnly));

        ThemeOptions.Clear();
        ThemeOptions.Add(new ProfileThemeOptionViewModel("light", false, "روشن", "پیش‌فرض کلینیک") { IsOn = settings.Theme == "light" });
        ThemeOptions.Add(new ProfileThemeOptionViewModel("dark", true, "تیره", "مناسب محیط کم‌نور") { IsOn = settings.Theme == "dark" });

        LookToggles.Clear();
        LookToggles.Add(new ProfileToggleViewModel("stDense", "حالت فشردهٔ جدول‌ها", "ارتفاع ردیف‌ها ۲۰٪ کم می‌شود", settings.DenseTables));
        LookToggles.Add(new ProfileToggleViewModel("stKbd", "نمایش راهنمای میان‌برها", "کیبوردهای میان‌بر زیر نوار ابزار", settings.ShortcutHints));
        LookToggles.Add(new ProfileToggleViewModel("stAnim", "انیمیشن مقدماتی ورود", "جلوهٔ ورود صفحات و پنل‌ها", settings.EntryMotion));

        SecurityToggles.Clear();
        SecurityToggles.Add(new ProfileToggleViewModel("st2fa", "ورود دومرحله‌ای", "این قابلیت به اتصال سرویس احراز هویت و پیامک نیاز دارد؛ هنوز فعال نیست.", settings.TwoFactor));
        TrackPreferences();
    }

    // ── Sub-nav tab definitions (reference TABS) ─────────────────────────

    private static readonly (string Id, string Label, string IconKey)[] TabCatalog =
    [
        ("overview", "نمای کلی", "IconDashboard"),
        ("profile", "حساب و اطلاعات تماس", "IconUsers"),
        ("sched", "تنظیمات نوبت‌دهی", "IconCalendar"),
        ("alerts", "اعلان‌ها", "IconBell"),
        ("look", "ظاهر و تم", "IconMoon"),
        ("sec", "امنیت و نشست‌ها", "IconShield"),
        ("access", "کاربران و دسترسی‌ها", "IconIdCard"),
    ];

    // ── Notification matrix catalog (reference NMAT) ─────────────────────

    private static readonly (string Id, string Label, string Sub)[] MatrixCatalog =
    [
        ("cancel", "لغو نوبت", "وقتی مالک یا پذیرش نوبتی را لغو می‌کند"),
        ("overdue", "چک سررسیدگذشته", "چک‌های وصول‌نشده پس از سررسید"),
        ("cheque", "چک در حال تسویه", "روزشمارش تا سررسید چک‌های دریافتی"),
        ("stock", "موجودی انبار بحرانی", "کالا زیر نقطهٔ سفارش‌گذاری"),
        ("vaccine", "یادآوری واکسن", "برنامهٔ پیشگیری مراجعین"),
    ];

    // ── Commands ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectTab(string? id)
    {
        if (string.IsNullOrEmpty(id) || id == ActiveTab)
        {
            return;
        }

        ActiveTab = id;
        foreach (var tab in Tabs)
        {
            tab.IsActive = tab.Id == ActiveTab;
        }

        OnPropertyChanged(nameof(IsOverviewTab));
        OnPropertyChanged(nameof(IsEditableTab));
        OnPropertyChanged(nameof(IsPreferencesTab));
        OnPropertyChanged(nameof(ActiveSectionTitle));
        OnPropertyChanged(nameof(ActiveSectionDescription));
        OnPropertyChanged(nameof(SaveButtonLabel));
        SaveMessage = string.Empty;
        NotifySaveState();
        OnPropertyChanged(nameof(IsProfileTab));
        OnPropertyChanged(nameof(IsScheduleTab));
        OnPropertyChanged(nameof(IsAlertsTab));
        OnPropertyChanged(nameof(IsLookTab));
        OnPropertyChanged(nameof(IsSecurityTab));
        OnPropertyChanged(nameof(IsAccessTab));
    }

    // ── Durable staff management ─────────────────────────────────────────

    private async Task ReloadStaffAsync(string? selectedId = null)
    {
        var directory = await _service.GetStaffAsync();
        _directory = directory;
        CanManageUsers = directory.CanManageUsers;
        CanManageAccess = directory.CanManageAccess;
        selectedId ??= SelectedStaff?.Id;
        Staff.Clear();
        RoleCards.Clear();
        foreach (var account in directory.Members)
        {
            var role = directory.Roles.FirstOrDefault(r => r.Id == account.RoleId);
            var member = new StaffMemberViewModel(account.MembershipId, account.Name, account.RoleId,
                role?.Label ?? account.RoleId, account.Username, InitialsOf(account.Name),
                account.RoleId == "Manager" ? "#7b5cc4" : "#1570cd", account.IsActive)
                { IsProtected = account.IsProtected, ChangeToken = account.ChangeToken };
            foreach (var permission in directory.Permissions)
                member.Permissions.Add(new StaffPermissionViewModel(permission.Code, permission.Label,
                    permission.Description, "IconShield", account.PermissionCodes.Contains(permission.Code)));
            Staff.Add(member);
        }
        foreach (var role in directory.Roles)
            RoleCards.Add(new StaffRoleCardViewModel(role.Id, role.Label, "کاربران ثبت‌شده در این نقش", "IconIdCard",
                directory.Members.Count(m => m.RoleId == role.Id), "#1570cd", "#eff8ff"));
        var options = directory.Roles.Select(r => r.Label).ToArray();
        var current = NewMemberRoleOptions.Value;
        NewMemberRoleOptions = new ProfileSelectViewModel(options.Length == 0 ? [""] : options,
            options.Contains(current) ? current : directory.Roles.FirstOrDefault(r => r.Id == "Doctor")?.Label ?? options.FirstOrDefault() ?? "");
        SelectedStaff = Staff.FirstOrDefault(m => m.Id == selectedId) ?? Staff.FirstOrDefault();
        OnPropertyChanged(nameof(StaffSummary));
        AccessMessage = !CanManageUsers && !CanManageAccess
            ? "اجازهٔ مدیریت کاربران و دسترسی‌ها را ندارید."
            : "اطلاعات از پایگاه داده خوانده شد. تغییر دسترسی‌ها نیاز به ذخیره دارد.";
    }

    private static string InitialsOf(string name)
    {
        var parts = name.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[0][..1]} {parts[1][..1]}" : name[..1];
    }

    private bool CanAddStaff() => CanManageUsers && !IsStaffBusy;
    private bool CanSaveAccess() => CanEditSelected;
    private bool CanRemoveStaff() => CanChangeSelectedStatus;

    [RelayCommand(CanExecute = nameof(CanAddStaff))]
    private async Task AddStaffAsync()
    {
        var role = _directory?.Roles.FirstOrDefault(r => r.Label == NewMemberRoleOptions.Value);
        NewMemberError = string.IsNullOrWhiteSpace(NewMemberName) ? "نام و نام خانوادگی را وارد کنید."
            : string.IsNullOrWhiteSpace(NewMemberUsername) ? "نام کاربری ورود به برنامه را وارد کنید."
            : string.IsNullOrWhiteSpace(NewMemberPassword) ? "رمز عبور ورود به برنامه را وارد کنید."
            : role is null ? "نقش کاربر را انتخاب کنید." : string.Empty;
        if (HasNewMemberError)
        {
            _toast.Show(NewMemberError, Tone.Warn);
            return;
        }
        var request = new CreateStaffRequest(NewMemberName.Trim(), NewMemberUsername.Trim(), NewMemberPassword,
            role!.Id, NewMemberPhone.Trim());
        await ChangeStaffAsync(async () =>
        {
            await _service.AddStaffAsync(request);
            NewMemberName = NewMemberPhone = NewMemberUsername = NewMemberPassword = string.Empty;
        }, "حساب کاربری با نقش انتخاب‌شده ثبت شد.");
    }

    [RelayCommand]
    private void SelectStaff(StaffMemberViewModel? member)
    {
        if (member is not null && !IsStaffBusy) SelectedStaff = member;
    }

    [RelayCommand(CanExecute = nameof(CanRemoveStaff))]
    private async Task RemoveStaffAsync(StaffMemberViewModel? member)
    {
        member ??= SelectedStaff;
        if (member is null || member.IsProtected) return;
        var active = !member.IsActive;
        await ChangeStaffAsync(() => _service.SetStaffActiveAsync(member.Id, active, member.ChangeToken),
            active ? "حساب کاربر فعال شد." : "حساب غیرفعال شد؛ سوابق کاربر محفوظ است.");
    }

    [RelayCommand(CanExecute = nameof(CanSaveAccess))]
    private void GrantAllPermissions()
    {
        if (SelectedStaff is null) return;
        foreach (var permission in SelectedStaff.Permissions) permission.IsGranted = true;
        AccessMessage = "تغییرات هنوز ذخیره نشده است.";
    }

    [RelayCommand(CanExecute = nameof(CanSaveAccess))]
    private void ResetPermissions()
    {
        if (SelectedStaff is null) return;
        var granted = _directory?.Roles.FirstOrDefault(r => r.Id == SelectedStaff.RoleId)?.DefaultPermissions ?? [];
        foreach (var permission in SelectedStaff.Permissions)
            permission.IsGranted = granted.Contains(permission.SectionId);
        AccessMessage = "پیش‌فرض نقش اعمال شد؛ برای ثبت، ذخیرهٔ دسترسی‌ها را بزنید.";
    }

    [RelayCommand(CanExecute = nameof(CanSaveAccess))]
    private async Task SaveAccessAsync()
    {
        var member = SelectedStaff;
        if (member is null) return;
        var codes = member.Permissions.Where(p => p.IsGranted).Select(p => p.SectionId).ToArray();
        await ChangeStaffAsync(() => _service.SaveStaffAccessAsync(member.Id, codes, member.ChangeToken), "دسترسی‌های کاربر ثبت شد.");
    }

    private async Task ChangeStaffAsync(System.Func<Task> change, string success)
    {
        IsStaffBusy = true;
        try
        {
            await change();
            _toast.Show(success, Tone.Ok);
            try { await ReloadStaffAsync(); AccessMessage = success; }
            catch (System.Exception) { AccessMessage = "ثبت انجام شد؛ دریافت فهرست جدید ناموفق بود. بازخوانی کنید."; }
        }
        catch (System.Exception ex)
        {
            AccessMessage = ex.Message switch
            {
                "CONCURRENCY_CONFLICT" => "اطلاعات این کاربر تغییر کرده است؛ بازخوانی کنید و دوباره ذخیره کنید.",
                "USERNAME_EXISTS" => "نام کاربری تکراری است؛ نام دیگری وارد کنید.",
                "PASSWORD_POLICY_VIOLATION" => "رمز عبور حداقل ۸ کاراکتر و شامل حرف و عدد باشد.",
                "PERMISSION_DENIED" => "مجوز این عملیات را ندارید یا حساب محافظت‌شده است.",
                "VALIDATION_FAILED" => "اطلاعات واردشده معتبر نیست؛ طول و مقدار فیلدها را بررسی کنید.",
                _ => "ذخیره ناموفق بود؛ تغییرات ثبت نشد. دوباره تلاش کنید.",
            };
            NewMemberError = AccessMessage;
            _toast.Show(AccessMessage, Tone.Warn);
        }
        finally { IsStaffBusy = false; }
    }

    [RelayCommand]
    private async Task RefreshStaffAsync()
    {
        if (IsStaffBusy) return;
        IsStaffBusy = true;
        try { await ReloadStaffAsync(); }
        catch (System.Exception) { AccessMessage = "بازخوانی اطلاعات ناموفق بود."; }
        finally { IsStaffBusy = false; }
    }

    [RelayCommand]
    private void ToggleDay(ProfileDayChipViewModel? chip)
    {
        if (chip is not null)
        {
            chip.IsOn = !chip.IsOn;
        }
    }

    [RelayCommand]
    private void PickTheme(ProfileThemeOptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        foreach (var theme in ThemeOptions)
        {
            theme.IsOn = theme.Id == option.Id;
        }
    }

    private void TrackPreferences()
    {
        IEnumerable<CommunityToolkit.Mvvm.ComponentModel.ObservableObject> editors =
            new CommunityToolkit.Mvvm.ComponentModel.ObservableObject[]
            { WorkFromOptions, WorkToOptions, DurationOptions, GapOptions, OverlapOptions,
              CalendarViewOptions, QuietFromOptions, QuietToOptions }
            .Concat(DayChips).Concat(OwnerNotifyToggles).Concat(CalendarDisplayToggles)
            .Concat(MatrixRows).Concat(QuietToggles).Concat(ThemeOptions).Concat(LookToggles);
        foreach (var editor in editors)
            editor.PropertyChanged += (_, _) => { if (!IsBusy) HasSettingsChanges = true; };
    }

    private bool CanSaveCurrentChanges() => !IsBusy && !IsSaving && IsEditableTab &&
        (IsProfileTab ? HasProfileChanges : HasSettingsChanges);

    [RelayCommand(CanExecute = nameof(CanSaveCurrentChanges))]
    private async Task SaveCurrentChangesAsync()
    {
        IsSaving = true;
        SaveMessage = string.Empty;
        try
        {
            if (IsProfileTab)
            {
                await SaveProfileAsync();
                HasProfileChanges = false;
            }
            else
            {
                await SaveSettingsAsync();
                HasSettingsChanges = false;
            }
            SaveMessage = "تغییرات با موفقیت ذخیره شد.";
        }
        catch (System.Exception)
        {
            SaveMessage = "ذخیره ناموفق بود؛ تغییرات شما در فرم باقی مانده است. دوباره تلاش کنید.";
            _toast.Show(SaveMessage, Tone.Warn);
        }
        finally { IsSaving = false; }
    }

    /// <summary>The reference's <c>PF.save()</c> — close nothing, just toast.</summary>
    private async Task SaveProfileAsync()
    {
        var profile = await _service.GetAsync();
        profile.Name = ProfileName;
        profile.Phone = ProfilePhone;
        profile.Email = ProfileEmail;

        await _service.SaveAsync(profile);

        _toast.Show("تغییرات ذخیره شد.", Tone.Ok);
    }

    /// <summary>The reference's header «ذخیره تغییرات» — persists every settings section.</summary>
    private async Task SaveSettingsAsync()
    {
        var settings = await _service.GetSettingsAsync();

        settings.WorkHoursFrom = WorkFromOptions.Value;
        settings.WorkHoursTo = WorkToOptions.Value;
        settings.WorkingDays = DayChips.Where(d => d.IsOn).Select(d => d.Index).ToList();
        settings.DefaultDuration = DurationOptions.Value;
        settings.RestGap = GapOptions.Value;
        settings.OverlapLimit = OverlapOptions.Value;
        settings.DefaultCalendarView = CalendarViewOptions.Value;

        settings.SmsReminder = OwnerNotifyToggles[0].IsOn;
        settings.SmsInstantConfirm = OwnerNotifyToggles[1].IsOn;
        settings.WaitlistNotify = OwnerNotifyToggles[2].IsOn;

        settings.ShowNowLine = CalendarDisplayToggles[0].IsOn;
        settings.ShowCancelled = CalendarDisplayToggles[1].IsOn;
        settings.ShowNewHint = CalendarDisplayToggles[2].IsOn;

        settings.NotificationMatrix = MatrixRows
            .Select(r => new NotificationChannelSetting { EventId = r.EventId, Sms = r.Sms, InApp = r.InApp })
            .ToList();

        settings.QuietFrom = QuietFromOptions.Value;
        settings.QuietTo = QuietToOptions.Value;
        settings.QuietCriticalOnly = QuietToggles[0].IsOn;

        settings.Theme = ThemeOptions.FirstOrDefault(t => t.IsOn)?.Id ?? "light";
        settings.DenseTables = LookToggles[0].IsOn;
        settings.ShortcutHints = LookToggles[1].IsOn;
        settings.EntryMotion = LookToggles[2].IsOn;

        settings.TwoFactor = SecurityToggles[0].IsOn;

        await _service.SaveSettingsAsync(settings);

        _toast.Show("تغییرات ذخیره شد.", Tone.Ok);
    }

    /// <summary>The reference's <c>PF.discard()</c> — toast + repaint.</summary>
    [RelayCommand]
    private async Task DiscardAsync()
    {
                if (IsSaving || IsStaffBusy) return;
        await LoadAsync();
        SaveMessage = "اطلاعات ذخیره‌شده دوباره بارگذاری شد.";
    }

    /// <summary>The reference's session sign-out (<c>data-endsess</c>) — toast only.</summary>
    [RelayCommand]
    private void EndSession()
    {
        _toast.Show("مدیریت نشست‌های سایر دستگاه‌ها پس از اتصال سرور در دسترس قرار می‌گیرد.", Tone.Info);
    }
}
