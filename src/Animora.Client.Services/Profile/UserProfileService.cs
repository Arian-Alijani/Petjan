using System.Globalization;
using System.Text.Json;
using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Profile;
using Animora.Contracts.Profile;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Profile;

/// <summary>
/// Real <see cref="IUserProfileService"/> (C10). Each call opens its own scope and dispatches through the local
/// store: the profile + settings are read from the real <c>User</c>/<c>StaffMembership</c>/<c>UserPreference</c>/
/// <c>LocalSessions</c> tables and the profile KPIs from durable, user-scoped counts — no sample data and no
/// mock branching. Persian labels are produced here (the Contracts shape is display text);
/// <see cref="HistoryText"/> does the same for the history feed.
/// </summary>
public sealed class UserProfileService(IServiceScopeFactory scopes) : IUserProfileService
{
    public async Task<UserProfile> GetAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, zones) = Resolve(scope);
        var context = provider.BuildQueryContext();
        if (context.IsFailure) return ProfileMapping.EmptyProfile;
        var result = await dispatcher.QueryAsync(new UserProfileQuery(), context.Value!);
        if (result.IsFailure) return ProfileMapping.EmptyProfile;
        var tenant = await scope.ServiceProvider.GetRequiredService<IIdentityRepository>()
            .FindTenantAsync(context.Value!.TenantId).ConfigureAwait(false);
        return ProfileMapping.ToContract(result.Value!, tenant?.DisplayName ?? string.Empty,
            zones.GetClinicTimeZone());
    }

    public async Task SaveAsync(UserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, _) = Resolve(scope);
        var context = await provider.BuildCommandContextAsync(null, null, null, default).ConfigureAwait(false);
        if (context.IsFailure) throw new InvalidOperationException(context.Error.Code);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AppSettingsKeys.ProfilePhone] = profile.Phone ?? string.Empty,
            [AppSettingsKeys.ProfileEmail] = profile.Email ?? string.Empty,
        };
        var result = await dispatcher.SendAsync(new SaveUserProfileCommand(values), context.Value!).ConfigureAwait(false);
        if (result.IsFailure) throw new InvalidOperationException(result.Error.Code);
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, _) = Resolve(scope);
        var context = provider.BuildQueryContext();
        if (context.IsFailure) return ProfileMapping.DefaultSettings();
        var result = await dispatcher.QueryAsync(new AppSettingsQuery(), context.Value!);
        return result.IsFailure ? ProfileMapping.DefaultSettings() : ProfileMapping.ToSettings(result.Value!);
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, _) = Resolve(scope);
        var context = await provider.BuildCommandContextAsync(null, null, null, default).ConfigureAwait(false);
        if (context.IsFailure) throw new InvalidOperationException(context.Error.Code);
        var result = await dispatcher
            .SendAsync(new SaveAppSettingsCommand(ProfileMapping.ToPreferenceBag(settings)), context.Value!)
            .ConfigureAwait(false);
        if (result.IsFailure) throw new InvalidOperationException(result.Error.Code);
    }

    public async Task<StaffDirectory> GetStaffAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, _) = Resolve(scope);
        var context = provider.BuildQueryContext();
        if (context.IsFailure) throw new InvalidOperationException(context.Error.Code);
        var result = await dispatcher.QueryAsync(new StaffDirectoryQuery(), context.Value!);
        if (result.IsFailure) throw new InvalidOperationException(result.Error.Code);
        var model = result.Value!;
        return new StaffDirectory(
            model.Members.Select(m => new StaffAccount(m.MembershipId, m.UserId, m.Name, m.Username,
                m.Role.ToString(), m.IsActive, m.Role == StaffRole.Manager || m.UserId == context.Value!.UserId,
                m.Permissions)).ToArray(),
            model.Roles.Select(r => new StaffRoleOption(r.Key.ToString(), ProfileMapping.RoleLabel(r.Key.ToString()),
                r.Value)).ToArray(),
            PermissionCodes.All.Select(code => new StaffPermissionOption(code, ProfileMapping.PermissionLabel(code),
                PermissionCodes.RiskOf(code) == RiskLevel.Critical ? "مجوز حساس؛ در حالت آفلاین محدود است" :
                PermissionCodes.RiskOf(code) == RiskLevel.High ? "عملیات حساس؛ اعتبار آفلاین محدود" : "مجوز مستقل این عملیات")).ToArray(),
            model.CanManageUsers, model.CanManageAccess);
    }

    public Task AddStaffAsync(CreateStaffRequest request)
    {
        if (!Enum.TryParse<StaffRole>(request.RoleId, out var role) || !Enum.IsDefined(role))
            throw new InvalidOperationException("VALIDATION_FAILED");
        return SendStaffAsync(new CreateStaffCommand(request.Name, request.Username, request.Password, role, request.Phone));
    }

    public Task SetStaffActiveAsync(string membershipId, bool active) =>
        SendStaffAsync(new SetStaffActiveCommand(membershipId, active));

    public Task SaveStaffAccessAsync(string membershipId, IReadOnlyList<string> permissionCodes) =>
        SendStaffAsync(new SaveStaffAccessCommand(membershipId, permissionCodes));

    private async Task SendStaffAsync(ICommand<Unit> command)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, _) = Resolve(scope);
        var context = await provider.BuildCommandContextAsync(null, null, null, default);
        if (context.IsFailure) throw new InvalidOperationException(context.Error.Code);
        var result = await dispatcher.SendAsync(command, context.Value!);
        if (result.IsFailure) throw new InvalidOperationException(result.Detail == "USERNAME_EXISTS"
            ? "USERNAME_EXISTS" : result.Error.Code);
    }

    private static (IDispatcher Dispatcher, ICurrentUserProvider Provider, IClinicTimeZoneProvider Zones) Resolve(
        AsyncServiceScope scope) =>
        (scope.ServiceProvider.GetRequiredService<IDispatcher>(),
         scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>(),
         scope.ServiceProvider.GetRequiredService<IClinicTimeZoneProvider>());
}

/// <summary>Key/args → contract mapping for the Profile screen; all Persian display text lives here.</summary>
internal static class ProfileMapping
{
    internal static readonly UserProfile EmptyProfile = new();

    /// <summary>Weekday labels (0 = شنبه … 6 = جمعه) shared with the settings screen.</summary>
    internal static readonly string[] Weekdays =
        ["شنبه", "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه"];

    internal static readonly string[] DefaultMatrixEventIds = ["cancel", "overdue", "cheque", "stock", "vaccine"];

    internal static UserProfile ToContract(UserProfileReadModel model, string tenantName, TimeZoneInfo zone)
    {
        var prefs = model.Preferences;
        var local = model.LastLoginUtc is { } login ? TimeZoneInfo.ConvertTime(login, zone) : (DateTimeOffset?)null;
        return new UserProfile
        {
            Name = model.DisplayName,
            RoleLabel = RoleLabel(model.Role),
            Specialty = string.Empty,
            BranchName = tenantName,
            Phone = Pref(prefs, AppSettingsKeys.ProfilePhone),
            Email = Pref(prefs, AppSettingsKeys.ProfileEmail),
            Initials = Initials(model.DisplayName),
            NationalCode = string.Empty,
            LastLoginTime = local is { } at ? at.ToString("HH:mm", CultureInfo.InvariantCulture) : string.Empty,
            Stats = model.Stats.Select(Stat).ToList(),
            Team = model.Team.Select(ToTeamMember).ToList(),
            Sessions = model.Sessions.Select(s => new ProfileSession
            {
                Name = s.IsCurrent ? "این دستگاه" : Fa(s.DeviceId),
                Detail = $"آخرین فعالیت: {Fa(s.LastActivityUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture))}",
                IsCurrent = s.IsCurrent,
            }).ToList(),
        };
    }

    internal static AppSettings ToSettings(IReadOnlyDictionary<string, string> prefs)
    {
        var defaults = DefaultSettings();
        return new AppSettings
        {
            WorkHoursFrom = Pref(prefs, AppSettingsKeys.WorkHoursFrom, defaults.WorkHoursFrom),
            WorkHoursTo = Pref(prefs, AppSettingsKeys.WorkHoursTo, defaults.WorkHoursTo),
            WorkingDays = IntList(prefs, AppSettingsKeys.WorkingDays) ?? defaults.WorkingDays,
            DefaultDuration = Pref(prefs, AppSettingsKeys.DefaultDuration, defaults.DefaultDuration),
            RestGap = Pref(prefs, AppSettingsKeys.RestGap, defaults.RestGap),
            OverlapLimit = Pref(prefs, AppSettingsKeys.OverlapLimit, defaults.OverlapLimit),
            DefaultCalendarView = Pref(prefs, AppSettingsKeys.DefaultCalendarView, defaults.DefaultCalendarView),
            SmsReminder = Bool(prefs, AppSettingsKeys.SmsReminder, defaults.SmsReminder),
            SmsInstantConfirm = Bool(prefs, AppSettingsKeys.SmsInstantConfirm, defaults.SmsInstantConfirm),
            WaitlistNotify = Bool(prefs, AppSettingsKeys.WaitlistNotify, defaults.WaitlistNotify),
            ShowNowLine = Bool(prefs, AppSettingsKeys.ShowNowLine, defaults.ShowNowLine),
            ShowCancelled = Bool(prefs, AppSettingsKeys.ShowCancelled, defaults.ShowCancelled),
            ShowNewHint = Bool(prefs, AppSettingsKeys.ShowNewHint, defaults.ShowNewHint),
            NotificationMatrix = Matrix(prefs) ?? defaults.NotificationMatrix,
            QuietFrom = Pref(prefs, AppSettingsKeys.QuietFrom, defaults.QuietFrom),
            QuietTo = Pref(prefs, AppSettingsKeys.QuietTo, defaults.QuietTo),
            QuietCriticalOnly = Bool(prefs, AppSettingsKeys.QuietCriticalOnly, defaults.QuietCriticalOnly),
            Theme = Pref(prefs, AppSettingsKeys.Theme, defaults.Theme),
            DenseTables = Bool(prefs, AppSettingsKeys.DenseTables, defaults.DenseTables),
            ShortcutHints = Bool(prefs, AppSettingsKeys.ShortcutHints, defaults.ShortcutHints),
            EntryMotion = Bool(prefs, AppSettingsKeys.EntryMotion, defaults.EntryMotion),
            TwoFactor = Bool(prefs, AppSettingsKeys.TwoFactor, defaults.TwoFactor),
        };
    }

    internal static Dictionary<string, string> ToPreferenceBag(AppSettings s) => new(StringComparer.Ordinal)
    {
        [AppSettingsKeys.WorkHoursFrom] = s.WorkHoursFrom,
        [AppSettingsKeys.WorkHoursTo] = s.WorkHoursTo,
        [AppSettingsKeys.WorkingDays] = JsonSerializer.Serialize(s.WorkingDays),
        [AppSettingsKeys.DefaultDuration] = s.DefaultDuration,
        [AppSettingsKeys.RestGap] = s.RestGap,
        [AppSettingsKeys.OverlapLimit] = s.OverlapLimit,
        [AppSettingsKeys.DefaultCalendarView] = s.DefaultCalendarView,
        [AppSettingsKeys.SmsReminder] = BoolText(s.SmsReminder),
        [AppSettingsKeys.SmsInstantConfirm] = BoolText(s.SmsInstantConfirm),
        [AppSettingsKeys.WaitlistNotify] = BoolText(s.WaitlistNotify),
        [AppSettingsKeys.ShowNowLine] = BoolText(s.ShowNowLine),
        [AppSettingsKeys.ShowCancelled] = BoolText(s.ShowCancelled),
        [AppSettingsKeys.ShowNewHint] = BoolText(s.ShowNewHint),
        [AppSettingsKeys.NotificationMatrix] = JsonSerializer.Serialize(s.NotificationMatrix),
        [AppSettingsKeys.QuietFrom] = s.QuietFrom,
        [AppSettingsKeys.QuietTo] = s.QuietTo,
        [AppSettingsKeys.QuietCriticalOnly] = BoolText(s.QuietCriticalOnly),
        [AppSettingsKeys.Theme] = s.Theme,
        [AppSettingsKeys.DenseTables] = BoolText(s.DenseTables),
        [AppSettingsKeys.ShortcutHints] = BoolText(s.ShortcutHints),
        [AppSettingsKeys.EntryMotion] = BoolText(s.EntryMotion),
        [AppSettingsKeys.TwoFactor] = BoolText(s.TwoFactor),
    };

    /// <summary>Every section's neutral default; a first load with no stored rows shows this, never null.</summary>
    internal static AppSettings DefaultSettings() => new()
    {
        WorkHoursFrom = "۰۷:۰۰",
        WorkHoursTo = "۲۲:۰۰",
        WorkingDays = [0, 1, 2, 3, 4, 5],
        DefaultDuration = "۳۰ دقیقه",
        RestGap = "۵ دقیقه",
        OverlapLimit = "بدون هم‌پوشانی",
        DefaultCalendarView = "هفته",
        NotificationMatrix = DefaultMatrixEventIds
            .Select(id => new NotificationChannelSetting { EventId = id, InApp = true })
            .ToList(),
        QuietFrom = "۲۲:۰۰",
        QuietTo = "۰۷:۰۰",
        Theme = "light",
        ShortcutHints = true,
        EntryMotion = true,
    };

    private static ProfileStat Stat(ProfileStatRow row)
    {
        var (icon, label, tone) = row.Id switch
        {
            UserProfileReadModel.AppointmentsStat => ("IconCalendar", "نوبت‌های من", ""),
            UserProfileReadModel.VisitsStat => ("IconCheck", "ویزیت‌های من", "warn"),
            UserProfileReadModel.VaccinationsStat => ("IconSyringe", "واکسن‌های من", "info"),
            _ => ("IconBell", "رویدادهای من", "violet"),
        };
        return new ProfileStat { IconKey = icon, Value = row.Value.ToString(CultureInfo.InvariantCulture), Label = label, Tone = tone };
    }

    private static ProfileTeamMember ToTeamMember(ProfileTeamRow row)
    {
        var (accent, roleTone) = Accent(row.Role);
        return new ProfileTeamMember
        {
            Name = row.DisplayName,
            Subtitle = Shift(row.WorkStartMinute, row.WorkEndMinute),
            Initials = Initials(row.DisplayName),
            AccentHex = accent,
            RoleLabel = RoleLabel(row.Role),
            RoleTone = roleTone,
        };
    }

    private static (string Accent, string Tone) Accent(string role) =>
        role == nameof(StaffRole.Doctor) ? ("#1570cd", "info")
        : role == nameof(StaffRole.Cashier) ? ("#0d9488", "info")
        : role == nameof(StaffRole.Manager) ? ("#7b5cc4", "info")
        : ("#e08a5c", "mute");

    internal static string PermissionLabel(string code) => code switch
    {
        PermissionCodes.PatientsRead => "پرونده‌ها · مشاهده",
        PermissionCodes.PatientsWrite => "پرونده‌ها · ویرایش",
        PermissionCodes.PatientsOwnerEdit => "پرونده‌ها · ویرایش مالک",
        PermissionCodes.PatientsPetEdit => "پرونده‌ها · ویرایش حیوان",
        PermissionCodes.ReceptionManage => "پذیرش · مدیریت صف",
        PermissionCodes.VisitsRead => "ویزیت · مشاهده",
        PermissionCodes.VisitsWrite => "ویزیت · معاینه و نسخه",
        PermissionCodes.VisitsComplete => "ویزیت · تکمیل و ارسال به صندوق",
        PermissionCodes.VaccinationRead => "واکسیناسیون · مشاهده",
        PermissionCodes.VaccinationWrite => "واکسیناسیون · ثبت تزریق",
        PermissionCodes.AppointmentsRead => "نوبت‌دهی · مشاهده",
        PermissionCodes.AppointmentsBook => "نوبت‌دهی · رزرو",
        PermissionCodes.AppointmentsMove => "نوبت‌دهی · جابه‌جایی",
        PermissionCodes.AppointmentsCancel => "نوبت‌دهی · لغو",
        PermissionCodes.BoardingRead => "پانسیون · مشاهده",
        PermissionCodes.BoardingManage => "پانسیون · مدیریت",
        PermissionCodes.BoardingCheckout => "پانسیون · ترخیص",
        PermissionCodes.InventoryRead => "انبار · مشاهده",
        PermissionCodes.InventoryAdjust => "انبار · اصلاح موجودی",
        PermissionCodes.InventoryReceive => "انبار · دریافت کالا",
        PermissionCodes.PurchasingRead => "خرید · مشاهده",
        PermissionCodes.PurchasingManage => "خرید · مدیریت سفارش",
        PermissionCodes.SalesRead => "فروش · مشاهده",
        PermissionCodes.SalesDraft => "فروش · ایجاد فاکتور",
        PermissionCodes.SalesPost => "فروش · ثبت نهایی",
        PermissionCodes.SalesVoid => "فروش · ابطال فاکتور",
        PermissionCodes.SalesRefund => "فروش · استرداد",
        PermissionCodes.SalesDiscountApprove => "فروش · تأیید تخفیف",
        PermissionCodes.PaymentsRegister => "پرداخت · ثبت دریافت",
        PermissionCodes.PaymentsReverse => "پرداخت · برگشت",
        PermissionCodes.CashierSessionOpen => "صندوق · بازکردن نشست",
        PermissionCodes.CashierSessionClose => "صندوق · بستن نشست",
        PermissionCodes.ChequesRead => "چک‌ها · مشاهده",
        PermissionCodes.ChequesManage => "چک‌ها · مدیریت",
        PermissionCodes.FinanceRead => "حسابداری · مشاهده",
        PermissionCodes.FinancePost => "حسابداری · ثبت سند",
        PermissionCodes.FinancePeriodClose => "حسابداری · بستن دوره",
        PermissionCodes.ReportsRead => "گزارش‌ها · مشاهده",
        PermissionCodes.SettingsRead => "تنظیمات · مشاهده",
        PermissionCodes.SettingsWrite => "تنظیمات · ویرایش",
        PermissionCodes.UsersManage => "مدیریت · کاربران",
        PermissionCodes.RolesManage => "مدیریت · دسترسی‌ها",
        PermissionCodes.DevicesManage => "مدیریت · دستگاه‌ها",
        PermissionCodes.IdentityStaffView => "تیم · مشاهده همکاران",
        PermissionCodes.AuditRead => "رویدادها · مشاهده گزارش ممیزی",
        _ => code,
    };

    internal static string RoleLabel(string role) => role switch
    {
        nameof(StaffRole.Manager) => "مدیر کلینیک",
        nameof(StaffRole.Doctor) => "پزشک حیوانات",
        nameof(StaffRole.Cashier) => "صندوق‌دار",
        nameof(StaffRole.Accountant) => "حسابدار",
        nameof(StaffRole.Receptionist) => "پذیرش",
        _ => "کارمند کلینیک",
    };

    private static string Shift(int startMinute, int endMinute) =>
        $"شیفت {Clock(startMinute)} تا {Clock(endMinute)}";

    private static string Clock(int minute) =>
        $"{minute / 60:D2}:{minute % 60:D2}";

    internal static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[0][..1]} {parts[1][..1]}" : name.Length > 0 ? name[..1] : "؟";
    }

    private static string Pref(IReadOnlyDictionary<string, string> prefs, string key, string fallback = "") =>
        prefs.TryGetValue(key, out var value) ? value : fallback;

    private static bool Bool(IReadOnlyDictionary<string, string> prefs, string key, bool fallback) =>
        prefs.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static string BoolText(bool value) => value ? "true" : "false";

    private static List<int>? IntList(IReadOnlyDictionary<string, string> prefs, string key)
    {
        if (!prefs.TryGetValue(key, out var json)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<int>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<NotificationChannelSetting>? Matrix(IReadOnlyDictionary<string, string> prefs)
    {
        if (!prefs.TryGetValue(AppSettingsKeys.NotificationMatrix, out var json)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<NotificationChannelSetting>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Persian digit rendering for the strings the View shows verbatim (dates/device ids).</summary>
    internal static string Fa(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var span = text.ToCharArray();
        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] is >= '0' and <= '9') span[i] = (char)('۰' + (span[i] - '0'));
        }

        return new string(span);
    }
}
