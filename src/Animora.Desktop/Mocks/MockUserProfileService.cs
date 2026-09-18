using System.Collections.Generic;
using System.Threading.Tasks;
using Animora.Contracts.Profile;

namespace Animora.Desktop.Mocks;

/// <summary>
/// In-memory implementation of <see cref="IUserProfileService"/> — the single
/// backend swap point for the Profile &amp; Settings screen (R-18). Returns the
/// same profile/settings shape the reference prototype renders
/// (Animora-UI/assets/profile.js), re-derived (not copied) and shaped for the
/// screen. Persists edits in memory only for the session, mirroring the
/// reference's explicit "settings live in memory only" design intent.
/// </summary>
public sealed class MockUserProfileService : IUserProfileService
{
    private UserProfile _profile = CreateSeedProfile();

    private AppSettings _settings = CreateSeedSettings();

    public Task<UserProfile> GetAsync() => Task.FromResult(_profile);

    public Task SaveAsync(UserProfile profile)
    {
        _profile = profile;
        return Task.CompletedTask;
    }

    public Task<AppSettings> GetSettingsAsync() => Task.FromResult(_settings);

    public Task SaveSettingsAsync(AppSettings settings)
    {
        _settings = settings;
        return Task.CompletedTask;
    }

    // ── Seed data (re-derived from the reference's USER/TEAM/SESS/NMAT) ──

    private static UserProfile CreateSeedProfile() => new()
    {
        Name = "دکتر آریا نیک",
        RoleLabel = "مدیر کلینیک",
        Specialty = "داخلی و جراحی",
        BranchName = "وت‌پلاس شعبهٔ مرکزی",
        Phone = "09123456789",
        Email = "aria.nik@vetplus.ir",
        Initials = "آ",
        NationalCode = "12458",
        LastLoginTime = "08:15",
        Stats =
        [
            new ProfileStat { IconKey = "IconCalendar", Value = "124", Label = "نوبت این ماه", Tone = "" },
            new ProfileStat { IconKey = "IconCheck", Value = "98", Suffix = "٪", Label = "نرخ پذیرش", Tone = "warn" },
            new ProfileStat { IconKey = "IconPaw", Value = "312", Label = "پروندهٔ فعال", Tone = "info" },
            new ProfileStat { IconKey = "IconBell", Value = "5", Label = "اعلان نخوانده", Tone = "violet" },
        ],
        Team =
        [
            new ProfileTeamMember { Name = "دکتر مینا همتی", Subtitle = "واکسن و پیشگیری · شیفت صبح", Initials = "م", AccentHex = "#1570cd", RoleLabel = "پزشک حیوانات", RoleTone = "info" },
            new ProfileTeamMember { Name = "دکتر سهیل کاویانی", Subtitle = "دندان‌پزشکی · شیفت عصر", Initials = "س", AccentHex = "#8f74d1", RoleLabel = "پزشک حیوانات", RoleTone = "info" },
            new ProfileTeamMember { Name = "نگار راد", Subtitle = "آرایش و نگهداری · شیفت صبح", Initials = "ن", AccentHex = "#e08a5c", RoleLabel = "کارشناس آرایش", RoleTone = "mute" },
            new ProfileTeamMember { Name = "سمیرا کاویانی", Subtitle = "مسئول صندوق · تمام‌وقت", Initials = "س", AccentHex = "#0d9488", RoleLabel = "صندوق‌دار", RoleTone = "mute" },
        ],
        Sessions =
        [
            new ProfileSession { Name = "کروم روی ویندوز — این دستگاه", Detail = "فعال · تهران", IsCurrent = true },
            new ProfileSession { Name = "اپلیکیشن پذیرش — تبلت صندوق", Detail = "آخرین فعالیت: امروز ۰۹:۴۲" },
            new ProfileSession { Name = "سافاری روی مک — منزل", Detail = "آخرین فعالیت: دیروز ۲۱:۱۰" },
        ],
    };

    private static AppSettings CreateSeedSettings() => new()
    {
        WorkHoursFrom = "۰۷:۰۰",
        WorkHoursTo = "۲۲:۰۰",
        WorkingDays = [0, 1, 2, 3, 4, 5],
        DefaultDuration = "۳۰ دقیقه",
        RestGap = "۵ دقیقه",
        OverlapLimit = "بدون هم‌پوشانی",
        DefaultCalendarView = "هفته",
        SmsReminder = true,
        SmsInstantConfirm = true,
        WaitlistNotify = false,
        ShowNowLine = true,
        ShowCancelled = true,
        ShowNewHint = true,
        NotificationMatrix =
        [
            new NotificationChannelSetting { EventId = "cancel", Sms = true, InApp = true },
            new NotificationChannelSetting { EventId = "overdue", Sms = true, InApp = true },
            new NotificationChannelSetting { EventId = "cheque", Sms = false, InApp = true },
            new NotificationChannelSetting { EventId = "stock", Sms = true, InApp = false },
            new NotificationChannelSetting { EventId = "vaccine", Sms = true, InApp = true },
        ],
        QuietFrom = "۲۲:۰۰",
        QuietTo = "۰۷:۰۰",
        QuietCriticalOnly = true,
        Theme = "light",
        DenseTables = false,
        ShortcutHints = true,
        EntryMotion = true,
        TwoFactor = true,
    };
}
