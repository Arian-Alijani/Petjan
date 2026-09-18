using System.Collections.Generic;

namespace Animora.Contracts.Profile;

/// <summary>
/// The signed-in user's profile as shown on the Profile &amp; Settings screen
/// (Animora-UI/assets/profile.js — the <c>USER</c> record plus the hero
/// identity card and the KPI strip it renders). Read-only display data:
/// name/role/specialty/contact/avatar plus the clinic team, the active
/// sessions and the profile KPI stats the reference shows. A future backend
/// implements the same shape — no View/ViewModel change (R-18).
/// </summary>
public sealed class UserProfile
{
    /// <summary>Full display name, e.g. "دکتر آریا نیک".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Role pill label, e.g. "مدیر کلینیک".</summary>
    public string RoleLabel { get; set; } = string.Empty;

    /// <summary>Specialty line, e.g. "داخلی و جراحی".</summary>
    public string Specialty { get; set; } = string.Empty;

    /// <summary>Branch name appended to the specialty line, e.g. "وت‌پلاس شعبهٔ مرکزی".</summary>
    public string BranchName { get; set; } = string.Empty;

    /// <summary>Contact phone (digits as stored; the View formats them Persian).</summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>Work e-mail address.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Avatar initials, e.g. "آ".</summary>
    public string Initials { get; set; } = string.Empty;

    /// <summary>National medical registration number (digits as stored).</summary>
    public string NationalCode { get; set; } = string.Empty;

    /// <summary>Last-login time of day, e.g. "08:15" (the View formats it Persian).</summary>
    public string LastLoginTime { get; set; } = string.Empty;

    /// <summary>The four KPI stats rendered in the strip under the hero.</summary>
    public List<ProfileStat> Stats { get; set; } = [];

    /// <summary>The clinic team members rendered in the «تیم کلینیک» card.</summary>
    public List<ProfileTeamMember> Team { get; set; } = [];

    /// <summary>The active sessions rendered in the «نشست‌های فعال» card.</summary>
    public List<ProfileSession> Sessions { get; set; } = [];
}

/// <summary>One KPI box of the profile strip (reference <c>hero()</c> kpis).</summary>
public sealed class ProfileStat
{
    /// <summary>Icon resource key, e.g. "IconCalendar".</summary>
    public string IconKey { get; set; } = string.Empty;

    /// <summary>Numeric value (digits as stored; the View formats it Persian).</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Optional value suffix rendered verbatim, e.g. "٪".</summary>
    public string Suffix { get; set; } = string.Empty;

    /// <summary>Caption under the value, e.g. "نوبت این ماه".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Accent tone: "" (brand) | "warn" | "info" | "violet".</summary>
    public string Tone { get; set; } = string.Empty;
}

/// <summary>One team member row of the «تیم کلینیک» card (reference <c>TEAM</c>).</summary>
public sealed class ProfileTeamMember
{
    public string Name { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;

    public string Initials { get; set; } = string.Empty;

    /// <summary>Avatar accent hex, e.g. "#1570cd".</summary>
    public string AccentHex { get; set; } = string.Empty;

    /// <summary>Role pill label, e.g. "پزشک حیوانات".</summary>
    public string RoleLabel { get; set; } = string.Empty;

    /// <summary>Role pill tone: "info" | "mute".</summary>
    public string RoleTone { get; set; } = "mute";
}

/// <summary>One active-session row of the «نشست‌های فعال» card (reference <c>SESS</c>).</summary>
public sealed class ProfileSession
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Detail line, e.g. "آخرین فعالیت: امروز ۰۹:۴۲".</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>True for the current session (shows the "این نشست" pill, hides the sign-out button).</summary>
    public bool IsCurrent { get; set; }
}

/// <summary>
/// Every editable preference the Profile &amp; Settings screen exposes,
/// enumerated from the reference's settings sections
/// (Animora-UI/assets/profile.js <c>ST</c>): scheduling, owner notifications,
/// calendar display, the app notification matrix, quiet hours, appearance and
/// security. Persisted in-memory only by the mock (the reference is an
/// explicit "settings live in memory only" demo).
/// </summary>
public sealed class AppSettings
{
    // ── Scheduling («ساعات و روزهای کاری» + «قوانین نوبت‌دهی») ──
    public string WorkHoursFrom { get; set; } = string.Empty;

    public string WorkHoursTo { get; set; } = string.Empty;

    /// <summary>Selected working weekdays (0 = شنبه … 6 = جمعه).</summary>
    public List<int> WorkingDays { get; set; } = [];

    public string DefaultDuration { get; set; } = string.Empty;

    public string RestGap { get; set; } = string.Empty;

    public string OverlapLimit { get; set; } = string.Empty;

    public string DefaultCalendarView { get; set; } = string.Empty;

    // ── Owner notifications («اطلاع‌رسانی به مالک») ──
    public bool SmsReminder { get; set; }

    public bool SmsInstantConfirm { get; set; }

    public bool WaitlistNotify { get; set; }

    // ── Calendar display («نمایش تقویم») ──
    public bool ShowNowLine { get; set; }

    public bool ShowCancelled { get; set; }

    public bool ShowNewHint { get; set; }

    // ── App notification matrix («ماتریس اعلان‌ها») ──
    public List<NotificationChannelSetting> NotificationMatrix { get; set; } = [];

    // ── Quiet hours («ساعات سکوت») ──
    public string QuietFrom { get; set; } = string.Empty;

    public string QuietTo { get; set; } = string.Empty;

    public bool QuietCriticalOnly { get; set; }

    // ── Appearance («حالت نمایش» + «چیدمان فشرده») ──
    /// <summary>"light" | "dark".</summary>
    public string Theme { get; set; } = "light";

    public bool DenseTables { get; set; }

    public bool ShortcutHints { get; set; }

    public bool EntryMotion { get; set; }

    // ── Security («امنیت حساب») ──
    public bool TwoFactor { get; set; }
}

/// <summary>
/// One row of the app notification matrix: which channels a given clinic event
/// is delivered through (reference <c>ST.nMat</c>).
/// </summary>
public sealed class NotificationChannelSetting
{
    /// <summary>Event id: cancel | overdue | cheque | stock | vaccine.</summary>
    public string EventId { get; set; } = string.Empty;

    public bool Sms { get; set; }

    public bool InApp { get; set; }
}
