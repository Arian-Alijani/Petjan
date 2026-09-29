namespace Animora.Application.UseCases.Profile;

/// <summary>
/// C10: the machine-readable shape of the Profile &amp; Settings screen. Everything the Desktop renders is
/// language-neutral: the role is the <c>StaffRole</c> name, stats are (id, value) pairs and sessions carry the
/// raw device/activity instants. Persian labels are produced only in <c>Animora.Client.Services</c> /
/// the Desktop (CONVENTIONS §4).
/// </summary>
public sealed record ProfileStatRow(string Id, long Value);

/// <summary>One real local session row (B03 <c>LocalSessions</c>); never a synthesized sample.</summary>
public sealed record ProfileSessionRow(
    string SessionId,
    string DeviceId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastActivityUtc,
    bool IsCurrent);

/// <summary>One clinic team member; <c>Role</c> is the <c>StaffRole</c> name and the shift is raw minutes.</summary>
public sealed record ProfileTeamRow(
    string UserId,
    string DisplayName,
    string Role,
    int WorkStartMinute,
    int WorkEndMinute);

/// <summary>
/// The profile read model: identity + stats + real sessions, the persisted preference bag and — only when the
/// caller holds <c>identity.staff.view</c> — the clinic team list.
/// </summary>
public sealed record UserProfileReadModel(
    string UserId,
    string UserName,
    string DisplayName,
    string Role,
    DateTimeOffset? LastLoginUtc,
    bool CanViewTeam,
    IReadOnlyDictionary<string, string> Preferences,
    IReadOnlyList<ProfileStatRow> Stats,
    IReadOnlyList<ProfileSessionRow> Sessions,
    IReadOnlyList<ProfileTeamRow> Team)
{
    /// <summary>The stat ids the Desktop maps to icon/label/tone.</summary>
    public const string AppointmentsStat = "profile.stat.appointments";
    public const string VisitsStat = "profile.stat.visits";
    public const string VaccinationsStat = "profile.stat.vaccinations";
    public const string FeedStat = "profile.stat.activity";
}

/// <summary>
/// The closed catalog of persisted preference keys. A save referencing anything outside this set fails with
/// <c>SETTING_KEY_UNKNOWN</c> — an unknown key is never silently stored.
/// </summary>
public static class AppSettingsKeys
{
    // ── Profile fields ──
    public const string ProfilePhone = "profile.phone";
    public const string ProfileEmail = "profile.email";

    // ── Scheduling ──
    public const string WorkHoursFrom = "settings.workHoursFrom";
    public const string WorkHoursTo = "settings.workHoursTo";
    public const string WorkingDays = "settings.workingDays";
    public const string DefaultDuration = "settings.defaultDuration";
    public const string RestGap = "settings.restGap";
    public const string OverlapLimit = "settings.overlapLimit";
    public const string DefaultCalendarView = "settings.defaultCalendarView";

    // ── Owner notifications ──
    public const string SmsReminder = "settings.smsReminder";
    public const string SmsInstantConfirm = "settings.smsInstantConfirm";
    public const string WaitlistNotify = "settings.waitlistNotify";

    // ── Calendar display ──
    public const string ShowNowLine = "settings.showNowLine";
    public const string ShowCancelled = "settings.showCancelled";
    public const string ShowNewHint = "settings.showNewHint";

    // ── Notification matrix + quiet hours ──
    public const string NotificationMatrix = "settings.notificationMatrix";
    public const string QuietFrom = "settings.quietFrom";
    public const string QuietTo = "settings.quietTo";
    public const string QuietCriticalOnly = "settings.quietCriticalOnly";

    // ── Appearance ──
    public const string Theme = "settings.theme";
    public const string DenseTables = "settings.denseTables";
    public const string ShortcutHints = "settings.shortcutHints";
    public const string EntryMotion = "settings.entryMotion";

    // ── Security ──
    public const string TwoFactor = "settings.twoFactor";

    /// <summary>Every key a settings save may write.</summary>
    public static readonly IReadOnlyList<string> SettingsKeys =
    [
        WorkHoursFrom, WorkHoursTo, WorkingDays, DefaultDuration, RestGap, OverlapLimit, DefaultCalendarView,
        SmsReminder, SmsInstantConfirm, WaitlistNotify,
        ShowNowLine, ShowCancelled, ShowNewHint,
        NotificationMatrix, QuietFrom, QuietTo, QuietCriticalOnly,
        Theme, DenseTables, ShortcutHints, EntryMotion,
        TwoFactor,
    ];

    /// <summary>Every key the profile save may write.</summary>
    public static readonly IReadOnlyList<string> ProfileKeys = [ProfilePhone, ProfileEmail];

    /// <summary><see langword="true"/> when <paramref name="key"/> is either a profile or a settings key.</summary>
    public static bool IsKnown(string key) =>
        !string.IsNullOrWhiteSpace(key) &&
        (Contains(SettingsKeys, key) || Contains(ProfileKeys, key));

    /// <summary><see langword="true"/> when <paramref name="key"/> is a settings (not profile) key.</summary>
    public static bool IsSettingKey(string key) =>
        !string.IsNullOrWhiteSpace(key) && Contains(SettingsKeys, key);

    private static bool Contains(IReadOnlyList<string> keys, string key)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            if (string.Equals(keys[i], key, StringComparison.Ordinal)) return true;
        }

        return false;
    }
}
