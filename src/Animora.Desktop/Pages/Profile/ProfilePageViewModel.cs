using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Profile;
using Animora.Desktop.Controls;
using Animora.Desktop.Services;
using Animora.Desktop.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Profile;

/// <summary>
/// ViewModel for the Profile &amp; Settings screen. Mirrors the reference's
/// page controller (Animora-UI/assets/profile.js): the hero identity card +
/// KPI strip (<c>hero()</c>), the settings sub-nav rail (<c>TABS</c> /
/// <c>navHtml</c>), and the five settings tab panels — profile, scheduling,
/// alerts, look, security (<c>tabProfile</c> / <c>tabSched</c> /
/// <c>tabAlerts</c> / <c>tabLook</c> / <c>tabSec</c>). The reference's
/// "save shows a toast and closes nothing" pattern is reproduced exactly:
/// <see cref="SaveProfileCommand"/> / <see cref="SaveSettingsCommand"/> call
/// the service then toast; <see cref="DiscardCommand"/> restores + toasts.
/// All data flows through <see cref="IUserProfileService"/> (the single
/// backend swap point, R-18) — no sample data or mock branching leaks into
/// this public shape.
/// </summary>
public sealed partial class ProfilePageViewModel : PageViewModelBase
{
    private readonly IUserProfileService _service;
    private readonly IToastService _toast;

    public ProfilePageViewModel(IUserProfileService service, IToastService toast)
    {
        _service = service;
        _toast = toast;

        Title = "پروفایل و تنظیمات";
        Eyebrow = "حساب کاربری";
        Breadcrumbs.Add("حساب کاربری");
        Breadcrumbs.Add("پروفایل و تنظیمات");

        foreach (var (id, label, iconKey) in TabCatalog)
        {
            Tabs.Add(new ProfileTabViewModel(id, label, iconKey) { IsActive = id == ActiveTab });
        }

        SeedAccessManagement();

        _ = LoadAsync();
    }

    // ── Chrome ───────────────────────────────────────────────────────────

    public string PageEyebrow => "حساب کاربری";

    public string PageTitle => "پروفایل و تنظیمات";

    public string PageSubtitle =>
        "اطلاعات کاربری، تنظیمات نوبت‌دهی، اعلان‌ها و ظاهر برنامه را مدیریت کنید.";

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
    private string _activeTab = "profile";

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

    /// <summary>The clinic staff roster (design-only sample data).</summary>
    public ObservableCollection<StaffMemberViewModel> Staff { get; } = [];

    /// <summary>Role-template summary cards (manager / doctor / secretary).</summary>
    public ObservableCollection<StaffRoleCardViewModel> RoleCards { get; } = [];

    /// <summary>Role choices for the «افزودن کاربر» form.</summary>
    public ProfileSelectViewModel NewMemberRoleOptions { get; } =
        new(["پزشک", "منشی", "مدیر", "دستیار"], "پزشک");

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
    }

    // ── Loading ──────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        IsBusy = true;

        try
        {
            var profile = await _service.GetAsync();
            var settings = await _service.GetSettingsAsync();

            ApplyProfile(profile);
            ApplySettings(settings);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyProfile(UserProfile profile)
    {
        UserName = profile.Name;
        UserRole = profile.RoleLabel;
        UserInitials = profile.Initials;
        UserRoleLine = $"{profile.Specialty} · {profile.BranchName}";
        UserPhone = ProfilePresentation.Fa(profile.Phone);
        UserEmail = profile.Email;
        LastLoginLine = $"آخرین ورود: امروز {ProfilePresentation.Fa(profile.LastLoginTime)}";
        NationalCodeLine = $"شمارهٔ نظام veterinarians {ProfilePresentation.Fa(profile.NationalCode)}";

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
        SecurityToggles.Add(new ProfileToggleViewModel("st2fa", "ورود دومرحله‌ای", "کد یک‌بارمصرف پیامکی هنگام ورود", settings.TwoFactor));
    }

    // ── Sub-nav tab definitions (reference TABS) ─────────────────────────

    private static readonly (string Id, string Label, string IconKey)[] TabCatalog =
    [
        ("profile", "پروفایل کاربری", "IconUsers"),
        ("sched", "تنظیمات نوبت‌دهی", "IconCalendar"),
        ("alerts", "اعلان‌ها", "IconBell"),
        ("look", "ظاهر و تم", "IconMoon"),
        ("sec", "امنیت و نشست‌ها", "IconShield"),
        ("access", "مدیریت دسترسی‌ها", "IconIdCard"),
    ];

    // ── Access-management catalogs (design-only sample data) ─────────────

    /// <summary>App sections a staff member's access can be toggled for.</summary>
    private static readonly (string Id, string Label, string Sub, string IconKey)[] SectionCatalog =
    [
        ("dashboard", "داشبورد", "نمای کلی و شاخص‌های کلینیک", "IconDashboard"),
        ("reception", "پذیرش", "صف انتظار و پذیرش مراجعین", "IconClipboard"),
        ("patients", "پرونده‌ها", "پرونده مالکان و حیوانات", "IconPaw"),
        ("visits", "ویزیت", "معاینه و ثبت نسخه", "IconStetho"),
        ("appointments", "نوبت‌دهی", "تقویم و رزرو نوبت", "IconCalendar"),
        ("sales", "فروش و صندوق", "صدور فاکتور و دریافت وجه", "IconReceipt"),
        ("inventory", "انبار", "موجودی کالا و دارو", "IconBox"),
        ("finance", "مالی و حسابداری", "هزینه‌ها، سود و چک‌ها", "IconChart"),
        ("settings", "تنظیمات و مدیریت", "دسترسی به این صفحه و مدیریت کاربران", "IconTools"),
    ];

    /// <summary>Which sections each role gets by default (design template).</summary>
    private static HashSet<string> DefaultSections(string roleId) => roleId switch
    {
        "manager" => ["dashboard", "reception", "patients", "visits", "appointments", "sales", "inventory", "finance", "settings"],
        "doctor" => ["dashboard", "patients", "visits", "appointments"],
        "secretary" => ["dashboard", "reception", "patients", "appointments", "sales"],
        _ => ["dashboard", "patients"],
    };

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

        OnPropertyChanged(nameof(IsProfileTab));
        OnPropertyChanged(nameof(IsScheduleTab));
        OnPropertyChanged(nameof(IsAlertsTab));
        OnPropertyChanged(nameof(IsLookTab));
        OnPropertyChanged(nameof(IsSecurityTab));
        OnPropertyChanged(nameof(IsAccessTab));
    }

    // ── Access-management: seed + commands (design-only) ─────────────────

    private void SeedAccessManagement()
    {
        RoleCards.Add(new StaffRoleCardViewModel(
            "manager", "مدیر", "دسترسی کامل به همهٔ بخش‌ها", "IconShield", 1, "#7b5cc4", "#f4f3ff"));
        RoleCards.Add(new StaffRoleCardViewModel(
            "doctor", "پزشک", "ویزیت، پرونده و نوبت‌دهی", "IconStetho", 2, "#1570cd", "#eff8ff"));
        RoleCards.Add(new StaffRoleCardViewModel(
            "secretary", "منشی", "پذیرش، نوبت و صندوق", "IconClipboard", 2, "#0e7566", "#f0fdfa"));

        AddStaffMember("مدیر کلینیک", "manager", "مدیر", "دسترسی کامل · آخرین ورود: امروز", "م ک", "#7b5cc4", true, select: true);
        AddStaffMember("دکتر آرمان رضایی", "doctor", "پزشک", "دامپزشک · شیفت صبح", "آ ر", "#1570cd", true);
        AddStaffMember("دکتر سارا محمدی", "doctor", "پزشک", "دامپزشک · شیفت عصر", "س م", "#0d9488", true);
        AddStaffMember("نگار احمدی", "secretary", "منشی", "پذیرش · شیفت صبح", "ن ا", "#0e7566", true);
        AddStaffMember("رضا کریمی", "secretary", "منشی", "پذیرش · شیفت عصر", "ر ک", "#b54708", false);
    }

    private void AddStaffMember(
        string name, string roleId, string roleLabel, string subtitle,
        string initials, string accentHex, bool isActive, bool select = false)
    {
        var member = new StaffMemberViewModel(
            "u" + (Staff.Count + 1), name, roleId, roleLabel, subtitle, initials, accentHex, isActive);

        var granted = DefaultSections(roleId);
        foreach (var (id, label, sub, iconKey) in SectionCatalog)
        {
            member.Permissions.Add(new StaffPermissionViewModel(id, label, sub, iconKey, granted.Contains(id)));
        }

        Staff.Add(member);

        if (select)
        {
            SelectedStaff = member;
        }
    }

    private static string RoleIdFromLabel(string label) => label switch
    {
        "پزشک" => "doctor",
        "منشی" => "secretary",
        "مدیر" => "manager",
        _ => "assistant",
    };

    private static string InitialsOf(string name)
    {
        var parts = name.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{parts[0][..1]} {parts[1][..1]}"
            : (name.Length > 0 ? name[..1] : "؟");
    }

    /// <summary>«افزودن کاربر جدید» — adds a staff member from the mini form.</summary>
    [RelayCommand]
    private void AddStaff()
    {
        var name = NewMemberName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            _toast.Show("نام کاربر را وارد کنید.", Tone.Warn);
            return;
        }

        var roleLabel = NewMemberRoleOptions.Value;
        var roleId = RoleIdFromLabel(roleLabel);
        var accents = new[] { "#1570cd", "#0d9488", "#0e7566", "#b54708", "#7b5cc4" };
        var accent = accents[Staff.Count % accents.Length];

        var subtitle = string.IsNullOrWhiteSpace(NewMemberPhone)
            ? $"{roleLabel} · تازه اضافه‌شده"
            : $"{roleLabel} · {ProfilePresentation.Fa(NewMemberPhone.Trim())}";

        AddStaffMember(name, roleId, roleLabel, subtitle, InitialsOf(name), accent, true, select: true);

        NewMemberName = string.Empty;
        NewMemberPhone = string.Empty;

        _toast.Show($"«{name}» به تیم اضافه شد (نمونه طراحی).", Tone.Ok);
    }

    /// <summary>Loads the tapped member's permission grid into the editor panel.</summary>
    [RelayCommand]
    private void SelectStaff(StaffMemberViewModel? member)
    {
        if (member is not null)
        {
            SelectedStaff = member;
        }
    }

    /// <summary>«حذف کاربر» — removes the selected member from the roster.</summary>
    [RelayCommand]
    private void RemoveStaff(StaffMemberViewModel? member)
    {
        member ??= SelectedStaff;
        if (member is null)
        {
            return;
        }

        if (member.RoleId == "manager")
        {
            _toast.Show("حساب مدیر اصلی قابل حذف نیست.", Tone.Warn);
            return;
        }

        var index = Staff.IndexOf(member);
        Staff.Remove(member);

        if (ReferenceEquals(SelectedStaff, member))
        {
            SelectedStaff = Staff.Count > 0 ? Staff[System.Math.Max(0, index - 1)] : null;
        }

        _toast.Show($"«{member.Name}» حذف شد (نمونه طراحی).", Tone.Info);
    }

    /// <summary>Grants the selected member every section at once.</summary>
    [RelayCommand]
    private void GrantAllPermissions()
    {
        if (SelectedStaff is null)
        {
            return;
        }

        foreach (var permission in SelectedStaff.Permissions)
        {
            permission.IsGranted = true;
        }
    }

    /// <summary>Resets the selected member to their role's default sections.</summary>
    [RelayCommand]
    private void ResetPermissions()
    {
        if (SelectedStaff is null)
        {
            return;
        }

        var granted = DefaultSections(SelectedStaff.RoleId);
        foreach (var permission in SelectedStaff.Permissions)
        {
            permission.IsGranted = granted.Contains(permission.SectionId);
        }

        _toast.Show("دسترسی‌ها به حالت پیش‌فرض نقش بازگشت.", Tone.Info);
    }

    /// <summary>Persists the selected member's access grid (design-only toast).</summary>
    [RelayCommand]
    private void SaveAccess()
    {
        if (SelectedStaff is null)
        {
            return;
        }

        _toast.Show($"دسترسی‌های «{SelectedStaff.Name}» ذخیره شد (نمونه طراحی).", Tone.Ok);
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

    /// <summary>The reference's <c>PF.save()</c> — close nothing, just toast.</summary>
    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        var profile = await _service.GetAsync();
        profile.Name = ProfileName;
        profile.Phone = ProfilePhone;
        profile.Email = ProfileEmail;

        await _service.SaveAsync(profile);

        _toast.Show("تنظیمات ذخیره شد (نمونه طراحی).", Tone.Ok);
    }

    /// <summary>The reference's header «ذخیره تغییرات» — persists every settings section.</summary>
    [RelayCommand]
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

        _toast.Show("تنظیمات ذخیره شد (نمونه طراحی).", Tone.Ok);
    }

    /// <summary>The reference's <c>PF.discard()</c> — toast + repaint.</summary>
    [RelayCommand]
    private async Task DiscardAsync()
    {
        _toast.Show("تغییرات بازگردانده شد (نمونه طراحی).", Tone.Info);
        await LoadAsync();
    }

    /// <summary>The reference's session sign-out (<c>data-endsess</c>) — toast only.</summary>
    [RelayCommand]
    private void EndSession()
    {
        _toast.Show("نشست انتخاب‌شده بسته شد (نمونه طراحی).", Tone.Info);
    }
}
