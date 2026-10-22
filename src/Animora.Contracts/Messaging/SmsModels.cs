using System;
using System.Collections.Generic;

namespace Animora.Contracts.Messaging;

// ─────────────────────────────────────────────────────────────────────────────
// «مدیریت پیامک» — contracts (DTOs + enums) for the SMS management screen.
//
// DESIGN-ONLY SEAM. This file only models the *shape* of SMS management state so
// the UI can be built and a real backend wired later. No message is ever sent
// from here, and no credit is actually computed — the production SmsService is a
// stub that returns design defaults. Defaults are therefore "off"/empty, and the
// UI must never claim that a message was sent.
//
// Several nearby subsystems already touch SMS (Vaccination SmsState/SmsTemplate,
// Appointments AppointmentSmsSettings, CustomerClub birthday SMS). Those are
// intentionally left untouched here; they are reconciled with this screen later.
// To stay decoupled, Messaging keeps its own small enums that *mirror* those
// (SmsDeliveryState ⇔ Vaccination.SmsState, SmsVaccineKind ⇔ Vaccination.TreatmentKind).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The business event that can trigger an SMS. This is the core key of the
/// per-event settings: each value gets its own enable switch + template, and
/// (for reminders) its own schedule.
/// </summary>
public enum SmsEventType
{
    /// <summary>«ثبت نوبت» — sent when an appointment is booked for a person.</summary>
    AppointmentBooked,

    /// <summary>«یادآوری نوبت» — sent before an appointment (configurable lead times + send hours).</summary>
    AppointmentReminder,

    /// <summary>«تشکر خرید» — sent after an invoice (sale) is registered.</summary>
    PurchaseThankYou,

    /// <summary>«ایجاد پرونده» — sent when a patient file is created.</summary>
    FileCreated,

    /// <summary>«یادآوری دارو و واکسن» — vaccine/medication reminder (three per-kind modes, configurable).</summary>
    VaccineReminder,
}

/// <summary>
/// Delivery state of one sent SMS. Mirrors <c>Animora.Contracts.Vaccination.SmsState</c>
/// (None/Sent/Scheduled/Failed) kept local so Messaging does not depend on Vaccination.
/// </summary>
public enum SmsDeliveryState
{
    None,
    Sent,
    Scheduled,
    Failed,
}

/// <summary>
/// Where/by whom a vaccine or medication is administered — the three reminder modes.
/// Mirrors <c>Animora.Contracts.Vaccination.TreatmentKind</c> 1:1 (ClinicVisit /
/// HomeByOwner / BoardingAdminister) kept local to avoid cross-context coupling.
/// </summary>
public enum SmsVaccineKind
{
    /// <summary>«مراجعه به کلینیک» — the patient comes to the clinic.</summary>
    ClinicVisit,

    /// <summary>«در خانه توسط صاحب» — the owner administers at home (an owner reminder applies).</summary>
    HomeByOwner,

    /// <summary>«در پانسیون توسط کلینیک» — the patient is boarded and the clinic administers there.</summary>
    BoardingAdminister,
}

// ─── Credit (Task 1) ─────────────────────────────────────────────────────────

/// <summary>
/// Remaining SMS credit. A provider may bill per-message (<see cref="RemainingCount"/>)
/// or by currency balance (<see cref="RemainingCreditMinor"/>, minor units). The seam
/// carries both; the UI shows whichever the provider populates. <see cref="AsOfUtc"/>
/// is <c>null</c> when unknown (the current stub), which the UI renders as
/// «در انتظار اتصال» rather than a real figure.
/// </summary>
public sealed record SmsCredit(
    long RemainingCount,
    long RemainingCreditMinor,
    DateTime? AsOfUtc)
{
    /// <summary>Design default: nothing known yet (stub).</summary>
    public static SmsCredit Unknown { get; } = new(0, 0, null);
}

// ─── Usage / charts ──────────────────────────────────────────────────────────

/// <summary>One day's SMS count — feeds the usage trend <c>BarChart</c>.</summary>
public sealed record SmsUsagePoint(DateTime Day, int Count);

/// <summary>Total SMS attributed to one event type — feeds the per-section donut/bar chart.</summary>
public sealed record SmsUsageBySection(SmsEventType EventType, int Count);

/// <summary>
/// The usage roll-up for the overview charts: the time trend, the per-section split
/// (chart requirement), and the grand total over the selected range.
/// </summary>
public sealed record SmsUsageReport(
    IReadOnlyList<SmsUsagePoint> Trend,
    IReadOnlyList<SmsUsageBySection> BySection,
    int TotalCount)
{
    /// <summary>Design default: no usage yet (stub).</summary>
    public static SmsUsageReport Empty { get; } = new([], [], 0);
}

// ─── Report (Task 2) ─────────────────────────────────────────────────────────

/// <summary>A preset time window for the report/usage reads (mirrors the «آمار» range chips).</summary>
public sealed record SmsReportRange(string RangeId = "30d")
{
    public static SmsReportRange Default { get; } = new();
}

/// <summary>One row of the «گزارش پیامک‌های ارسالی» table.</summary>
public sealed record SentSmsRow(
    string Id,
    DateTime WhenUtc,
    SmsEventType EventType,
    string Recipient,
    string Phone,
    string Body,
    int SegmentCount,
    SmsDeliveryState State);

/// <summary>One page of sent-SMS rows (server-side paged like the History/Statistics seams).</summary>
public sealed record SentSmsPage(
    IReadOnlyList<SentSmsRow> Rows,
    int Page,
    int PageSize,
    int TotalCount)
{
    /// <summary>Design default: an empty first page (stub).</summary>
    public static SentSmsPage Empty { get; } = new([], 1, 20, 0);
}

// ─── Settings (Tasks 3 & 6) ──────────────────────────────────────────────────

/// <summary>
/// Per-event SMS preference: on/off + the template body. The body carries token
/// placeholders substituted by the future backend, e.g. <c>{owner} {pet} {date}
/// {amount} {clinic}</c>. Counting follows the 70-chars-per-segment rule used
/// elsewhere (Vaccination SmsPreview).
/// </summary>
public sealed record SmsEventSetting(
    SmsEventType EventType,
    bool Enabled,
    string TemplateBody);

/// <summary>
/// Schedule for the appointment reminder: one or more lead times (e.g. [24, 2] =
/// 24h and 2h before), and the allowed send hours of day (messages due outside the
/// window are deferred into it). Both empty in the default = nothing scheduled.
/// </summary>
public sealed record ReminderSchedule(
    IReadOnlyList<int> LeadHoursList,
    IReadOnlyList<int> SendAtHours)
{
    /// <summary>Design default: no leads, standard daytime send window (9..20).</summary>
    public static ReminderSchedule Default { get; } = new([], [9, 20]);
}

/// <summary>
/// Vaccine/medication reminder setting for ONE <see cref="SmsVaccineKind"/>. The
/// three modes are configured independently. <see cref="LeadDaysList"/> allows
/// multiple reminders (e.g. [7, 1] = 7 days and 1 day before); <see cref="SendAtHours"/>
/// bounds the time of day.
/// </summary>
public sealed record VaccineReminderSetting(
    SmsVaccineKind Kind,
    bool Enabled,
    IReadOnlyList<int> LeadDaysList,
    IReadOnlyList<int> SendAtHours,
    string TemplateBody);

/// <summary>
/// The full, clinic-editable SMS settings persisted by the backend later.
/// <see cref="Events"/> holds one entry per <see cref="SmsEventType"/>;
/// <see cref="AppointmentReminder"/> carries the reminder schedule; and
/// <see cref="VaccineReminders"/> holds exactly three entries (one per
/// <see cref="SmsVaccineKind"/>). Everything is disabled by default so nothing
/// ever implies a real send.
/// </summary>
public sealed record SmsSettings(
    IReadOnlyList<SmsEventSetting> Events,
    ReminderSchedule AppointmentReminder,
    IReadOnlyList<VaccineReminderSetting> VaccineReminders)
{
    /// <summary>Default Persian template texts (design placeholders, not sent).</summary>
    public static SmsSettings Default { get; } = new(
        Events:
        [
            new(SmsEventType.AppointmentBooked, false, "{owner} عزیز، نوبت {pet} در تاریخ {date} ثبت شد. {clinic}"),
            new(SmsEventType.AppointmentReminder, false, "{owner} عزیز، یادآوری نوبت {pet} در تاریخ {date} ساعت {time}. {clinic}"),
            new(SmsEventType.PurchaseThankYou, false, "{owner} عزیز، از خرید شما سپاسگزاریم. مبلغ فاکتور: {amount}. {clinic}"),
            new(SmsEventType.FileCreated, false, "{owner} عزیز، پرونده {pet} با موفقیت ایجاد شد. {clinic}"),
            new(SmsEventType.VaccineReminder, false, "{owner} عزیز، یادآوری {vaccine} برای {pet} در تاریخ {date}. {clinic}"),
        ],
        AppointmentReminder: ReminderSchedule.Default,
        VaccineReminders:
        [
            new(SmsVaccineKind.ClinicVisit, false, [], [9, 20], "{owner} عزیز، نوبت {vaccine} {pet} در کلینیک، تاریخ {date}. {clinic}"),
            new(SmsVaccineKind.HomeByOwner, false, [], [9, 20], "{owner} عزیز، زمان {vaccine} {pet} در خانه فرا رسیده ({date}). {clinic}"),
            new(SmsVaccineKind.BoardingAdminister, false, [], [9, 20], "{owner} عزیز، {vaccine} {pet} در پانسیون در تاریخ {date} انجام می‌شود. {clinic}"),
        ]);
}
