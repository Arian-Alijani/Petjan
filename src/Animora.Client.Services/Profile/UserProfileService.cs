using System.Globalization;
using System.Net;
using System.Text.Json;
using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Profile;
using Animora.Client.Services.Identity;
using Animora.Contracts.Profile;
using Animora.Contracts.Wire.Auth;
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
///
/// <para><b>Staff management is server-authoritative when the server is configured.</b> Account creation, enable/
/// disable, permission overrides and the roster go straight to the central server (<c>/auth/staff</c>, synchronously,
/// like sign-in) so a new user exists for every device and can sign in; the server's failure is surfaced to the caller
/// (no silent local-only success). Only a build with no server configured keeps the local-store command path.</para>
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
        if (context.IsFailure) throw new ServiceException(context.Error);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AppSettingsKeys.ProfilePhone] = profile.Phone ?? string.Empty,
            [AppSettingsKeys.ProfileEmail] = profile.Email ?? string.Empty,
        };
        var result = await dispatcher.SendAsync(new SaveUserProfileCommand(values), context.Value!).ConfigureAwait(false);
        if (result.IsFailure) throw new ServiceException(result.Error);
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
        if (context.IsFailure) throw new ServiceException(context.Error);
        var result = await dispatcher
            .SendAsync(new SaveAppSettingsCommand(ProfileMapping.ToPreferenceBag(settings)), context.Value!)
            .ConfigureAwait(false);
        if (result.IsFailure) throw new ServiceException(result.Error);
    }

    public async Task<ClinicAccountStatus> GetAccountStatusAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>().BuildQueryContext();
        if (context.IsFailure) throw new ServiceException(context.Error);
        // Licensing and SMS integrations are not implemented yet. Unknown is not an active/free subscription.
        var source = scope.ServiceProvider.GetService<IClinicAccountStatusSource>();
        return source is null ? new ClinicAccountStatus() : await source.GetAsync().ConfigureAwait(false);
    }

    public async Task<StaffDirectory> GetStaffAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<OnlineApi>() is { } api) return await GetServerStaffAsync(api).ConfigureAwait(false);
        var (dispatcher, provider, _) = Resolve(scope);
        var context = provider.BuildQueryContext();
        if (context.IsFailure) throw new ServiceException(context.Error);
        var result = await dispatcher.QueryAsync(new StaffDirectoryQuery(), context.Value!);
        if (result.IsFailure) throw new ServiceException(result.Error);
        var model = result.Value!;
        return new StaffDirectory(
            model.Members.Select(m => new StaffAccount(m.MembershipId, m.UserId, m.Name, m.Username,
                m.Role.ToString(), m.IsActive, m.Role == StaffRole.Manager || m.UserId == context.Value!.UserId,
                m.Permissions, m.ChangeToken)).ToArray(),
            model.Roles.Select(r => new StaffRoleOption(r.Key.ToString(), ProfileMapping.RoleLabel(r.Key.ToString()),
                r.Value)).ToArray(),
            ProfileMapping.PermissionOptions(),
            model.CanManageUsers, model.CanManageAccess);
    }

    public async Task AddStaffAsync(CreateStaffRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.TryParse<StaffRole>(request.RoleId, out var role) || !Enum.IsDefined(role))
            throw new InvalidOperationException("VALIDATION_FAILED");
        if (await TryGetOnlineApiAsync().ConfigureAwait(false) is { } api)
        {
            await PostStaffAsync(api, "auth/staff", new CreateStaffServerRequest(request.Name, request.Username,
                request.Password, request.RoleId, request.Phone)).ConfigureAwait(false);
            return;
        }

        await SendStaffAsync(new CreateStaffCommand(request.Name, request.Username, request.Password, role, request.Phone))
            .ConfigureAwait(false);
    }

    public async Task SetStaffActiveAsync(string membershipId, bool active, string? expectedChangeToken = null)
    {
        if (await TryGetOnlineApiAsync().ConfigureAwait(false) is { } api)
        {
            await PostStaffAsync(api, $"auth/staff/{Uri.EscapeDataString(membershipId)}/active",
                new StaffActiveServerRequest(membershipId, active, expectedChangeToken)).ConfigureAwait(false);
            return;
        }

        await SendStaffAsync(new SetStaffActiveCommand(membershipId, active, expectedChangeToken)).ConfigureAwait(false);
    }

    public async Task SaveStaffAccessAsync(string membershipId, IReadOnlyList<string> permissionCodes, string? expectedChangeToken = null)
    {
        if (await TryGetOnlineApiAsync().ConfigureAwait(false) is { } api)
        {
            await PostStaffAsync(api, "auth/staff/access",
                new StaffAccessServerRequest(membershipId, permissionCodes, expectedChangeToken)).ConfigureAwait(false);
            return;
        }

        await SendStaffAsync(new SaveStaffAccessCommand(membershipId, permissionCodes, expectedChangeToken)).ConfigureAwait(false);
    }

    public async Task ApproveStaffDeviceAsync(string deviceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        var api = await TryGetOnlineApiAsync().ConfigureAwait(false) ?? throw new ServiceException(ErrorCodes.NetworkUnavailable);
        await PostStaffAsync(api, $"auth/device/{Uri.EscapeDataString(deviceId)}/approve", new { }, ct).ConfigureAwait(false);
    }

    public async Task RevokeStaffDeviceAsync(string deviceId, string reason, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var api = await TryGetOnlineApiAsync().ConfigureAwait(false) ?? throw new ServiceException(ErrorCodes.NetworkUnavailable);
        await PostStaffAsync(api, $"auth/device/{Uri.EscapeDataString(deviceId)}/revoke", new DeviceRevokeRequest(reason.Trim()), ct)
            .ConfigureAwait(false);
    }

    public async Task ReinstateStaffDeviceAsync(string deviceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        var api = await TryGetOnlineApiAsync().ConfigureAwait(false) ?? throw new ServiceException(ErrorCodes.NetworkUnavailable);
        await PostStaffAsync(api, $"auth/device/{Uri.EscapeDataString(deviceId)}/reinstate", new { }, ct).ConfigureAwait(false);
    }

    /// <summary>The shared online client, or <see langword="null"/> when no server is configured (offline/local-only build).</summary>
    private async Task<OnlineApi?> TryGetOnlineApiAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        return scope.ServiceProvider.GetService<OnlineApi>();
    }

    private static async Task<StaffDirectory> GetServerStaffAsync(OnlineApi api)
    {
        var exchange = await api.SendAsync(HttpMethod.Get, "auth/staff", body: null, bearer: null).ConfigureAwait(false);
        using var response = exchange.Response;
        if (response is null) throw new ServiceException(ErrorCodes.NetworkUnavailable);
        if (!response.IsSuccessStatusCode) throw await ServerFailureAsync(response).ConfigureAwait(false);
        var wire = await OnlineApi.ReadAsync<StaffDirectoryWire>(response).ConfigureAwait(false)
                   ?? throw new ServiceException(ErrorCodes.Unexpected);
        return new StaffDirectory(
            wire.Members.Select(m => new StaffAccount(m.MembershipId, m.UserId, m.Name, m.Username, m.Role,
                m.IsActive, m.IsProtected, m.Permissions, m.ChangeToken)).ToArray(),
            wire.Roles.Select(r => new StaffRoleOption(r.RoleId, ProfileMapping.RoleLabel(r.RoleId),
                r.DefaultPermissions)).ToArray(),
            ProfileMapping.PermissionOptions(),
            wire.CanManageUsers, wire.CanManageAccess,
            (wire.Devices ?? []).Select(d => new StaffDevice(d.DeviceId, d.UserId, d.DeviceName, d.Platform, d.Status)).ToArray(),
            wire.CanManageDevices);
    }

    /// <summary>POSTs a staff command to the server; any non-success surfaces as a <see cref="ServiceException"/> with the stable code.</summary>
    private static async Task PostStaffAsync(OnlineApi api, string path, object body, CancellationToken ct = default)
    {
        var exchange = await api.SendAsync(HttpMethod.Post, path, body, bearer: null, ct).ConfigureAwait(false);
        using var response = exchange.Response;
        if (response is null) throw new ServiceException(ErrorCodes.NetworkUnavailable);
        if (!response.IsSuccessStatusCode) throw await ServerFailureAsync(response, ct).ConfigureAwait(false);
    }

    /// <summary>Maps a failed server response to its wire error code, falling back to the HTTP status when the body has none.</summary>
    private static async Task<ServiceException> ServerFailureAsync(HttpResponseMessage response, CancellationToken ct = default)
    {
        var code = await OnlineApi.ReadErrorCodeAsync(response, ct).ConfigureAwait(false);
        if (code is not null) return new ServiceException(code, CategoryOf(response.StatusCode));
        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new ServiceException(ErrorCodes.SessionExpired),
            HttpStatusCode.Forbidden => new ServiceException(ErrorCodes.PermissionDenied),
            HttpStatusCode.NotFound => new ServiceException(ErrorCodes.NotFound),
            HttpStatusCode.Conflict => new ServiceException(ErrorCodes.ConcurrencyConflict),
            HttpStatusCode.BadRequest => new ServiceException(ErrorCodes.ValidationFailed),
            _ when OnlineApi.IsTransient(response.StatusCode) => new ServiceException(ErrorCodes.ServerBusy),
            _ => new ServiceException(ErrorCodes.Unexpected),
        };
    }

    private static string CategoryOf(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest => ErrorCode.Validation,
        HttpStatusCode.NotFound => ErrorCode.NotFound,
        HttpStatusCode.Conflict => ErrorCode.Conflict,
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ErrorCode.Forbidden,
        _ => ErrorCode.Infrastructure,
    };

    private async Task SendStaffAsync(ICommand<Unit> command)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider, _) = Resolve(scope);
        var context = await provider.BuildCommandContextAsync(null, null, null, default);
        if (context.IsFailure) throw new ServiceException(context.Error);
        var result = await dispatcher.SendAsync(command, context.Value!);
        if (result.IsFailure) throw new ServiceException(result.Error);
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
        PermissionCodes.PatientsOwnerEdit => "پرونده‌ها · ویرایش سرپرست",
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

    /// <summary>Every permission with its Persian label and risk description (shared by the local and server roster paths).</summary>
    internal static IReadOnlyList<StaffPermissionOption> PermissionOptions() =>
        PermissionCodes.All.Select(code => new StaffPermissionOption(code, PermissionLabel(code),
            PermissionCodes.RiskOf(code) == RiskLevel.Critical ? "مجوز حساس؛ در حالت آفلاین محدود است" :
            PermissionCodes.RiskOf(code) == RiskLevel.High ? "عملیات حساس؛ اعتبار آفلاین محدود" : "مجوز مستقل این عملیات")).ToArray();

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
