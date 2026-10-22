using System;
using System.Globalization;
using Animora.Contracts.Messaging;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;

namespace Animora.Desktop.Pages.Messaging;

/// <summary>Persian presentation helpers shared by the «مدیریت پیامک» sections (labels, colours, counts).</summary>
public static class SmsFormat
{
    /// <summary>Characters per SMS segment (Persian/UCS-2), the same rule the Vaccination SMS preview uses.</summary>
    public const int SegmentLength = 70;

    /// <summary>Supported template tokens (substituted by the future backend).</summary>
    public const string TokenHint = "{owner} نام صاحب · {pet} نام حیوان · {date} تاریخ · {time} ساعت · {amount} مبلغ · {vaccine} دارو/واکسن · {clinic} نام کلینیک";

    public static string EventName(SmsEventType type) => type switch
    {
        SmsEventType.AppointmentBooked => "ثبت نوبت",
        SmsEventType.AppointmentReminder => "یادآوری نوبت",
        SmsEventType.PurchaseThankYou => "تشکر خرید",
        SmsEventType.FileCreated => "ایجاد پرونده",
        SmsEventType.VaccineReminder => "یادآوری دارو و واکسن",
        _ => "سایر",
    };

    public static string EventHint(SmsEventType type) => type switch
    {
        SmsEventType.AppointmentBooked => "بلافاصله پس از ثبت نوبت برای صاحب حیوان ارسال می‌شود.",
        SmsEventType.AppointmentReminder => "پیش از زمان نوبت، در مراحل و ساعت‌های تعیین‌شده ارسال می‌شود.",
        SmsEventType.PurchaseThankYou => "پس از ثبت فاکتور فروش برای مشتری ارسال می‌شود.",
        SmsEventType.FileCreated => "هنگام ایجاد پروندهٔ جدید برای صاحب حیوان ارسال می‌شود.",
        SmsEventType.VaccineReminder => "یادآوری نوبت دارو و واکسن؛ برای هر سه حالت جداگانه تنظیم می‌شود.",
        _ => string.Empty,
    };

    public static string EventIcon(SmsEventType type) => type switch
    {
        SmsEventType.AppointmentBooked => "IconCalendarPlus",
        SmsEventType.AppointmentReminder => "IconClock",
        SmsEventType.PurchaseThankYou => "IconReceipt",
        SmsEventType.FileCreated => "IconFolder",
        SmsEventType.VaccineReminder => "IconSyringe",
        _ => "IconSms",
    };

    public static string EventColor(SmsEventType type) => type switch
    {
        SmsEventType.AppointmentBooked => "#4a6cf7",
        SmsEventType.AppointmentReminder => "#1570cd",
        SmsEventType.PurchaseThankYou => "#027a48",
        SmsEventType.FileCreated => "#b54708",
        SmsEventType.VaccineReminder => "#7a5af8",
        _ => "#98a2b3",
    };

    public static string VaccineKindName(SmsVaccineKind kind) => kind switch
    {
        SmsVaccineKind.ClinicVisit => "مراجعه به کلینیک",
        SmsVaccineKind.HomeByOwner => "در خانه توسط صاحب",
        SmsVaccineKind.BoardingAdminister => "در پانسیون توسط کلینیک",
        _ => "سایر",
    };

    public static string VaccineKindHint(SmsVaccineKind kind) => kind switch
    {
        SmsVaccineKind.ClinicVisit => "حیوان برای تزریق یا دریافت دارو به کلینیک مراجعه می‌کند.",
        SmsVaccineKind.HomeByOwner => "صاحب حیوان دارو را در خانه می‌دهد؛ یادآوری برای صاحب ارسال می‌شود.",
        SmsVaccineKind.BoardingAdminister => "حیوان در پانسیون است و کلینیک دارو یا واکسن را تجویز می‌کند.",
        _ => string.Empty,
    };

    public static string VaccineKindIcon(SmsVaccineKind kind) => kind switch
    {
        SmsVaccineKind.ClinicVisit => "IconStetho",
        SmsVaccineKind.HomeByOwner => "IconHome2",
        SmsVaccineKind.BoardingAdminister => "IconHome",
        _ => "IconSyringe",
    };

    public static string StateName(SmsDeliveryState state) => state switch
    {
        SmsDeliveryState.Sent => "ارسال‌شده",
        SmsDeliveryState.Scheduled => "زمان‌بندی‌شده",
        SmsDeliveryState.Failed => "ناموفق",
        _ => "نامشخص",
    };

    public static Tone StateTone(SmsDeliveryState state) => state switch
    {
        SmsDeliveryState.Sent => Tone.Ok,
        SmsDeliveryState.Scheduled => Tone.Info,
        SmsDeliveryState.Failed => Tone.Danger,
        _ => Tone.Neutral,
    };

    /// <summary>Number of SMS segments for <paramref name="length"/> characters (0 for an empty body).</summary>
    public static int Segments(int length) => length <= 0 ? 0 : (length + SegmentLength - 1) / SegmentLength;

    public static string Count(long value) => PersianNumberFormatter.FormatGrouped(value);

    public static string Digits(int value) => PersianNumberFormatter.ToPersianDigits(value.ToString(CultureInfo.InvariantCulture));

    public static string JalaliDateTime(DateTime utc)
    {
        var local = utc.Kind == DateTimeKind.Utc ? utc.ToLocalTime() : utc;
        return $"{JalaliDate.FromDateTime(local).ToString("short")} · {PersianNumberFormatter.ToPersianDigits(local.ToString("HH:mm", CultureInfo.InvariantCulture))}";
    }

    public static string JalaliDay(DateTime day) => JalaliDate.FromDateTime(day).ToString("medium");

    public static string JalaliDayFull(DateTime day) => JalaliDate.FromDateTime(day).ToString("long");
}
