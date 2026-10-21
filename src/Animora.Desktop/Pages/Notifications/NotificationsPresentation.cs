using System;
using System.Globalization;
using Animora.Contracts.Notifications;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.Patients;

namespace Animora.Desktop.Pages.Notifications;

/// <summary>Persian labels, colour/icon mapping and time wording of the notifications screens (no data access).</summary>
internal static class NotificationsPresentation
{
    public static string KindLabel(NotificationKind kind) => kind switch
    {
        NotificationKind.Info => "اطلاعیه",
        NotificationKind.Success => "موفقیت",
        NotificationKind.Warning => "هشدار",
        NotificationKind.Promo => "مناسبتی",
        NotificationKind.System => "سیستمی",
        NotificationKind.Reward => "جایزه",
        _ => "اطلاعیه",
    };

    /// <summary>Icon key (Icons.axaml) + accent / soft-background hex of a message kind.</summary>
    public static (string IconKey, string Accent, string Soft) Palette(NotificationKind kind) => kind switch
    {
        NotificationKind.Success => ("IconCheckCircle", "#027a48", "#ecfdf3"),
        NotificationKind.Warning => ("IconAlert", "#b54708", "#fffaeb"),
        NotificationKind.Promo => ("IconMegaphone", "#6938ef", "#f4f3ff"),
        NotificationKind.System => ("IconShield", "#475467", "#f2f4f7"),
        NotificationKind.Reward => ("IconSpark", "#b7791f", "#fff7e0"),
        _ => ("IconInfo", "#1570cd", "#eff8ff"),
    };

    /// <summary>«هم‌اکنون» / «N دقیقه پیش» / «N ساعت پیش» / Jalali date + clinic-local time.</summary>
    public static string FormatWhen(DateTimeOffset whenUtc, DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        var delta = nowUtc - whenUtc;
        if (delta < TimeSpan.FromMinutes(1)) return "هم‌اکنون";
        if (delta < TimeSpan.FromHours(1))
            return $"{PersianNumberFormatter.ToPersianDigits((int)delta.TotalMinutes)} دقیقه پیش";
        if (delta < TimeSpan.FromHours(24))
            return $"{PersianNumberFormatter.ToPersianDigits((int)delta.TotalHours)} ساعت پیش";
        var local = TimeZoneInfo.ConvertTime(whenUtc, zone);
        var time = PersianNumberFormatter.ToPersianDigits(local.ToString("HH:mm", CultureInfo.InvariantCulture));
        return $"{JalaliDate.FromDateTime(local.DateTime).ToString("short")} - {time}";
    }

    /// <summary>The user-facing text of a failed service call (validation has fixed wording).</summary>
    public static string Failure(InvalidOperationException ex, string fallback)
    {
        if (ex is Animora.Client.Services.ServiceException se)
        {
            if (se.Code == "VALIDATION_FAILED") return "اطلاعات واردشده معتبر نیست؛ فیلدها را بررسی کنید.";
        }

        return PatientsPresentation.CommandFailureMessage(ex, fallback);
    }
}
