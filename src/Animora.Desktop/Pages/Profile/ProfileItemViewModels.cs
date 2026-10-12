using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Profile;

/// <summary>
/// Presentation helpers for the Profile &amp; Settings screen: Persian-digit
/// formatting and hex→brush conversion — kept here so the ViewModel/View stay
/// free of formatting literals and the Contracts stay free of UI resources.
/// </summary>
public static class ProfilePresentation
{
    public static string Fa(string? text) =>
        PersianNumberFormatter.ToPersianDigits(text ?? string.Empty);

    /// <summary>A solid fill brush from a hex string (shared via <see cref="BrushCache"/>).</summary>
    public static IBrush Fill(string hex) => BrushCache.Solid(hex);

    /// <summary>The four KPI-strip accent tones (reference profile.css .pf-kpi[.t-*]).</summary>
    public static (IBrush Background, IBrush Foreground) KpiTone(string tone) => tone switch
    {
        "warn" => (Fill("#fffaeb"), Fill("#b54708")),
        "info" => (Fill("#eff8ff"), Fill("#1570cd")),
        "violet" => (Fill("#f4f3ff"), Fill("#7b5cc4")),
        _ => (Fill("#f0fdfa"), Fill("#0e7566")),
    };

    public static readonly IReadOnlyList<string> Weekdays =
        ["شنبه", "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه"];
}

/// <summary>One KPI box of the strip under the hero (reference <c>hero()</c>).</summary>
public sealed class ProfileKpiViewModel
{
    public ProfileKpiViewModel(string iconKey, string value, string suffix, string label, string tone)
    {
        IconKey = iconKey;
        Value = ProfilePresentation.Fa(value) + suffix;
        Label = label;
        Tone = tone;
        var (bg, fg) = ProfilePresentation.KpiTone(tone);
        IconBackground = bg;
        IconForeground = fg;
    }

    public string IconKey { get; }

    public string Value { get; }

    public string Label { get; }

    public string Tone { get; }

    public IBrush IconBackground { get; }

    public IBrush IconForeground { get; }
}

/// <summary>One team-member row of the «تیم کلینیک» card (reference <c>TEAM</c>).</summary>
public sealed class ProfileTeamMemberViewModel
{
    public ProfileTeamMemberViewModel(
        string name, string subtitle, string initials, string accentHex, string roleLabel, string roleTone)
    {
        Name = name;
        Subtitle = subtitle;
        Initials = initials;
        AvatarBrush = ProfilePresentation.Fill(accentHex);
        RoleLabel = roleLabel;
        RoleTone = roleTone;
        (RoleBackground, RoleForeground) = roleTone == "info"
            ? (ProfilePresentation.Fill("#eff8ff"), ProfilePresentation.Fill("#1570cd"))
            : (ProfilePresentation.Fill("#eef2f0"), ProfilePresentation.Fill("#536b68"));
    }

    public string Name { get; }

    public string Subtitle { get; }

    public string Initials { get; }

    public IBrush AvatarBrush { get; }

    public string RoleLabel { get; }

    /// <summary>"info" | "mute" — maps to the Phase 02 pill classes.</summary>
    public string RoleTone { get; }

    public IBrush RoleBackground { get; }

    public IBrush RoleForeground { get; }
}

/// <summary>One active-session row of the «نشست‌های فعال» card (reference <c>SESS</c>).</summary>
public sealed class ProfileSessionViewModel : ObservableObject
{
    public ProfileSessionViewModel(string name, string detail, bool isCurrent)
    {
        Name = name;
        Detail = detail;
        IsCurrent = isCurrent;
    }

    public string Name { get; }

    public string Detail { get; }

    public bool IsCurrent { get; }

    public bool CanSignOut => !IsCurrent;
}

/// <summary>A working-day chip of the «روزهای کاری هفته» group (reference <c>JDOW</c>).</summary>
public sealed partial class ProfileDayChipViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isOn;

    public ProfileDayChipViewModel(int index, string label)
    {
        Index = index;
        Label = label;
    }

    public int Index { get; }

    public string Label { get; }
}

/// <summary>One row of the app notification matrix (reference <c>NMAT</c> + <c>ST.nMat</c>).</summary>
public sealed partial class ProfileMatrixRowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _sms;

    [ObservableProperty]
    private bool _inApp;

    public ProfileMatrixRowViewModel(string eventId, string label, string sub, bool sms, bool inApp)
    {
        EventId = eventId;
        Label = label;
        Sub = sub;
        _sms = sms;
        _inApp = inApp;
    }

    public string EventId { get; }

    public string Label { get; }

    public string Sub { get; }
}

/// <summary>A theme-preview option of the «حالت نمایش» card (reference <c>.pf-topt</c>).</summary>
public sealed partial class ProfileThemeOptionViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isOn;

    public ProfileThemeOptionViewModel(string id, bool dark, string title, string sub)
    {
        Id = id;
        IsDark = dark;
        Title = title;
        Sub = sub;
        PreviewBackground = ProfilePresentation.Fill(dark ? "#1b2426" : "#f6f8f9");
    }

    /// <summary>"light" | "dark".</summary>
    public string Id { get; }

    public bool IsDark { get; }

    public string Title { get; }

    public string Sub { get; }

    /// <summary>The swatch preview fill (.pf-topt.light/.dark .pf-tg).</summary>
    public IBrush PreviewBackground { get; }
}

/// <summary>A sub-nav tab of the settings rail (reference <c>TABS</c>).</summary>
public sealed partial class ProfileTabViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public ProfileTabViewModel(string id, string label, string iconKey)
    {
        Id = id;
        Label = label;
        IconKey = iconKey;
    }

    public string Id { get; }

    public string Label { get; }

    public string IconKey { get; }
}

/// <summary>A titled option list for one settings select (.pb-select).</summary>
public sealed partial class ProfileSelectViewModel : ObservableObject
{
    public ProfileSelectViewModel(IReadOnlyList<string> options, string selected)
    {
        Options = options;
        SelectedIndex = System.Linq.Enumerable.ToList(options).IndexOf(selected);
        if (SelectedIndex < 0)
        {
            SelectedIndex = 0;
        }
    }

    public IReadOnlyList<string> Options { get; }

    [ObservableProperty]
    private int _selectedIndex;

    public string Value => SelectedIndex >= 0 && SelectedIndex < Options.Count ? Options[SelectedIndex] : string.Empty;
}

/// <summary>A single settings toggle row (reference <c>.switch-row</c>).</summary>
public sealed partial class ProfileToggleViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isOn;

    public ProfileToggleViewModel(string id, string label, string sub, bool isOn)
    {
        Id = id;
        Label = label;
        Sub = sub;
        _isOn = isOn;
    }

    public string Id { get; }

    public string Label { get; }

    public string Sub { get; }
}

// ═════════════════════════════════════════════════════════════════════════
//  Access-management tab (مدیریت دسترسی‌ها) — durable roster presentation.
//  Lets the clinic manager add staff (doctor / secretary / …), assign a
//  role, and toggle each staff member's access to the app's sections.
// ═════════════════════════════════════════════════════════════════════════

/// <summary>
/// One staff member of the «کاربران و دسترسی‌ها» roster. Selecting a member
/// loads their per-section permission grid on the right.
/// </summary>
public sealed partial class StaffMemberViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isActive;

    public StaffMemberViewModel(
        string id, string name, string roleId, string roleLabel,
        string subtitle, string initials, string accentHex, bool isActive)
    {
        Id = id;
        Name = name;
        RoleId = roleId;
        RoleLabel = roleLabel;
        Subtitle = subtitle;
        Initials = initials;
        AvatarBrush = ProfilePresentation.Fill(accentHex);
        _isActive = isActive;

        (RoleBackground, RoleForeground) = roleId switch
        {
            "Manager" => (ProfilePresentation.Fill("#f4f3ff"), ProfilePresentation.Fill("#7b5cc4")),
            "Doctor" => (ProfilePresentation.Fill("#eff8ff"), ProfilePresentation.Fill("#1570cd")),
            "Receptionist" => (ProfilePresentation.Fill("#f0fdfa"), ProfilePresentation.Fill("#0e7566")),
            _ => (ProfilePresentation.Fill("#eef2f0"), ProfilePresentation.Fill("#536b68")),
        };
    }

    public bool IsProtected { get; init; }

    public string ChangeToken { get; init; } = string.Empty;

    public string Id { get; }

    public string Name { get; }

    /// <summary>"manager" | "doctor" | "secretary" | custom.</summary>
    public string RoleId { get; }

    public string RoleLabel { get; }

    public string Subtitle { get; }

    public string Initials { get; }

    public IBrush AvatarBrush { get; }

    public IBrush RoleBackground { get; }

    public IBrush RoleForeground { get; }

    /// <summary>Per-section access toggles shown when this member is selected.</summary>
    public ObservableCollection<StaffPermissionViewModel> Permissions { get; } = [];
}

/// <summary>
/// One access-permission row in the selected staff member's grid — maps a
/// single app section to whether this member may open it.
/// </summary>
public sealed partial class StaffPermissionViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isGranted;

    public StaffPermissionViewModel(string sectionId, string label, string sub, string iconKey, bool isGranted)
    {
        SectionId = sectionId;
        Label = label;
        Sub = sub;
        IconKey = iconKey;
        _isGranted = isGranted;
    }

    public string SectionId { get; }

    public string Label { get; }

    public string Sub { get; }

    public string IconKey { get; }
}

/// <summary>
/// A role-template card in the «نقش‌ها» summary column (manager / doctor /
/// secretary). Purely informational in this design pass.
/// </summary>
public sealed class StaffRoleCardViewModel
{
    public StaffRoleCardViewModel(
        string id, string label, string sub, string iconKey, int memberCount, string accentHex, string tintHex)
    {
        Id = id;
        Label = label;
        Sub = sub;
        IconKey = iconKey;
        MemberCountLine = ProfilePresentation.Fa(memberCount.ToString()) + " کاربر";
        AccentBrush = ProfilePresentation.Fill(accentHex);
        AccentBackground = ProfilePresentation.Fill(tintHex);
    }

    public string Id { get; }

    public string Label { get; }

    public string Sub { get; }

    public string IconKey { get; }

    public string MemberCountLine { get; }

    public IBrush AccentBrush { get; }

    public IBrush AccentBackground { get; }
}

/// <summary>
/// One registered staff device in the manager's «دستگاه‌ها» card (TASK-3): a <c>Pending</c> device can be approved, a
/// non-revoked one revoked. Status text is Persian here; the wire carries only the stable status name.
/// </summary>
public sealed class StaffDeviceViewModel(string deviceId, string ownerName, string deviceName, string platform, string status)
{
    public string DeviceId { get; } = deviceId;

    public string OwnerName { get; } = ownerName;

    public string Title => $"{OwnerName} · {DeviceLabel}";

    /// <summary>The operator-visible device name, falling back to a Persian placeholder for legacy "Unnamed device" rows.</summary>
    private string DeviceLabel =>
        string.IsNullOrWhiteSpace(deviceName) || string.Equals(deviceName, "Unnamed device", System.StringComparison.OrdinalIgnoreCase)
            ? "دستگاه بدون‌نام"
            : deviceName;

    /// <summary>
    /// Human-readable system line. The wire platform is now like <c>"windows · Windows 10.0.19045"</c>; the leading family
    /// code is mapped to Persian and the OS version kept. Legacy placeholder rows ("unknown"/empty) never show a blank line.
    /// </summary>
    public string Detail => DescribeSystem(platform);

    internal static string DescribeSystem(string? platform)
    {
        string value = (platform ?? string.Empty).Trim();
        if (value.Length == 0 || string.Equals(value, "unknown", System.StringComparison.OrdinalIgnoreCase))
        {
            return "سیستم: نامشخص";
        }

        // Split the stable family code from the appended OS description ("windows · Windows 10.0.19045").
        string family = value;
        string? detail = null;
        int sep = value.IndexOf('·');
        if (sep >= 0)
        {
            family = value[..sep].Trim();
            detail = value[(sep + 1)..].Trim();
        }

        string familyLabel = family.ToLowerInvariant() switch
        {
            "windows" => "ویندوز",
            "macos" => "مک‌اواس",
            "linux" => "لینوکس",
            _ => family,
        };

        return string.IsNullOrWhiteSpace(detail) ? $"سیستم: {familyLabel}" : $"سیستم: {familyLabel} ({detail})";
    }

    public bool IsPending { get; } = string.Equals(status, "Pending", System.StringComparison.OrdinalIgnoreCase);

    public bool IsRevoked { get; } = string.Equals(status, "Revoked", System.StringComparison.OrdinalIgnoreCase);

    public bool CanRevoke => !IsRevoked;

    public string StatusLabel => IsPending ? "در انتظار تأیید" : IsRevoked ? "لغوشده" : "فعال";
}
