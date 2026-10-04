using System.Globalization;
using Animora.Contracts.History;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Avalonia.Media;

namespace Animora.Desktop.Pages.History;

/// <summary>
/// Presentation adapters for the History screen: the module-kind registry
/// (label / icon key / accent color / tone), the status-chip registry
/// (label / tone class / dot) and the number/money/date formatters — all
/// transcribed from the reference (Animora-UI/assets/history.js
/// <c>CATEGORIES</c> + <c>STATUS</c>, and the values in history.css /
/// styles.css :root). Kept here so the ViewModel and View stay free of
/// hardcoded literals and the Contracts stay free of UI resources.
/// </summary>
public static class HistoryPresentation
{
    // ── Number / money formatting ────────────────────────────────────────

    /// <summary>Persian digits for a plain integer (the reference's <c>fa</c>).</summary>
    public static string Fa(long value) =>
        PersianNumberFormatter.ToPersianDigits(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Persian digits for a text value, preserving non-digit characters.</summary>
    public static string FaText(string? text) =>
        PersianNumberFormatter.ToPersianDigits(text ?? string.Empty);

    /// <summary>The reference's <c>money(n)</c> — a Persian-grouped integer.</summary>
    public static string Money(long value) => PersianNumberFormatter.FormatGrouped(value);

    /// <summary>A solid fill brush from a hex string (shared via <see cref="BrushCache"/>).</summary>
    public static IBrush Fill(string hex) => BrushCache.Solid(hex);

    // ── Module-kind registry (the reference's CATEGORIES) ────────────────

    /// <summary>Icon resource key per kind (maps to Resources/Theme/Icons.axaml).</summary>
    public static string KindIcon(HistoryEventKind kind) => kind switch
    {
        HistoryEventKind.Sale => "IconCart",
        HistoryEventKind.Appointment => "IconCalendar",
        HistoryEventKind.Patient => "IconPaw",
        HistoryEventKind.Vaccine => "IconSyringe",
        HistoryEventKind.Expense => "IconReceipt",
        HistoryEventKind.Inventory => "IconBox",
        HistoryEventKind.Cheque => "IconBank",
        HistoryEventKind.Boarding => "IconHome",
        HistoryEventKind.Visit => "IconClipboard",
        HistoryEventKind.Purchase => "IconTruck",
        _ => "IconReceipt",
    };

    /// <summary>The kind's exact Persian label (the reference's <c>CATEGORIES[k].label</c>).</summary>
    public static string KindLabel(HistoryEventKind kind) => kind switch
    {
        HistoryEventKind.Sale => "فروش / درآمد",
        HistoryEventKind.Appointment => "نوبت",
        HistoryEventKind.Patient => "ثبت بیمار",
        HistoryEventKind.Vaccine => "واکسن",
        HistoryEventKind.Expense => "هزینه",
        HistoryEventKind.Inventory => "انبار / کالا",
        HistoryEventKind.Cheque => "چک",
        HistoryEventKind.Boarding => "پانسیون",
        HistoryEventKind.Visit => "ویزیت",
        HistoryEventKind.Purchase => "خرید / تأمین",
        _ => "رویداد",
    };

    /// <summary>The kind icon badge's accent text color (the reference's <c>CATEGORIES[k].color</c>).</summary>
    public static string KindColor(HistoryEventKind kind) => kind switch
    {
        HistoryEventKind.Sale => "#027a48",
        HistoryEventKind.Appointment => "#1570cd",
        HistoryEventKind.Patient => "#a48afb",
        HistoryEventKind.Vaccine => "#027a48",
        HistoryEventKind.Expense => "#d92d20",
        HistoryEventKind.Inventory => "#b54708",
        HistoryEventKind.Cheque => "#667085",
        HistoryEventKind.Boarding => "#a48afb",
        HistoryEventKind.Visit => "#1570cd",
        HistoryEventKind.Purchase => "#b54708",
        _ => "#667085",
    };

    /// <summary>The kind icon badge's background (the reference uses the matching <c>--*-bg</c> token).</summary>
    public static string KindBg(HistoryEventKind kind) => kind switch
    {
        HistoryEventKind.Sale => "#ecfdf3",
        HistoryEventKind.Appointment => "#eff8ff",
        HistoryEventKind.Patient => "#f4f3ff",
        HistoryEventKind.Vaccine => "#ecfdf3",
        HistoryEventKind.Expense => "#fef3f2",
        HistoryEventKind.Inventory => "#fffaeb",
        HistoryEventKind.Cheque => "#f1f5f6",
        HistoryEventKind.Boarding => "#f4f3ff",
        HistoryEventKind.Visit => "#eff8ff",
        HistoryEventKind.Purchase => "#fffaeb",
        _ => "#f1f5f6",
    };

    /// <summary>All kinds in the reference's declared order (drives the category chip row).</summary>
    public static IReadOnlyList<HistoryEventKind> AllKinds { get; } =
    [
        HistoryEventKind.Sale,
        HistoryEventKind.Appointment,
        HistoryEventKind.Patient,
        HistoryEventKind.Vaccine,
        HistoryEventKind.Expense,
        HistoryEventKind.Inventory,
        HistoryEventKind.Cheque,
        HistoryEventKind.Boarding,
        HistoryEventKind.Visit,
        HistoryEventKind.Purchase,
    ];

    // ── Status registry (the reference's STATUS) ─────────────────────────

    /// <summary>The status chip's exact Persian label.</summary>
    public static string StatusLabel(HistoryEventStatus status) => status switch
    {
        HistoryEventStatus.Paid => "پرداخت شد",
        HistoryEventStatus.Partial => "نیمه‌پرداخت",
        HistoryEventStatus.Unpaid => "پرداخت نشده",
        HistoryEventStatus.Done => "انجام شد",
        HistoryEventStatus.Cancelled => "لغو شده",
        HistoryEventStatus.Confirmed => "تأیید شده",
        HistoryEventStatus.Pending => "در انتظار",
        HistoryEventStatus.New => "جدید",
        HistoryEventStatus.Overdue => "معوق",
        HistoryEventStatus.Cleared => "تسویه شد",
        HistoryEventStatus.Bounced => "برگشت خورد",
        HistoryEventStatus.LowStock => "موجودی کم",
        HistoryEventStatus.Normal => "عادی",
        HistoryEventStatus.Discharge => "ترخیص",
        HistoryEventStatus.CheckIn => "ورود",
        _ => "عادی",
    };

    /// <summary>
    /// The status chip's tone class (the reference's <c>STATUS[s].cls</c>):
    /// ok | warn | danger | info | mute | lilac.
    /// </summary>
    public static string StatusClass(HistoryEventStatus status) => status switch
    {
        HistoryEventStatus.Paid => "ok",
        HistoryEventStatus.Partial => "warn",
        HistoryEventStatus.Unpaid => "danger",
        HistoryEventStatus.Done => "ok",
        HistoryEventStatus.Cancelled => "danger",
        HistoryEventStatus.Confirmed => "ok",
        HistoryEventStatus.Pending => "warn",
        HistoryEventStatus.New => "info",
        HistoryEventStatus.Overdue => "danger",
        HistoryEventStatus.Cleared => "ok",
        HistoryEventStatus.Bounced => "danger",
        HistoryEventStatus.LowStock => "warn",
        HistoryEventStatus.Normal => "mute",
        HistoryEventStatus.Discharge => "ok",
        HistoryEventStatus.CheckIn => "info",
        _ => "mute",
    };

    /// <summary>The tone's background fill (the <c>--*-bg</c> token that matches <see cref="StatusClass"/>).</summary>
    public static string StatusBg(HistoryEventStatus status) => status switch
    {
        HistoryEventStatus.Paid => "#ecfdf3",
        HistoryEventStatus.Partial => "#fffaeb",
        HistoryEventStatus.Unpaid => "#fef3f2",
        HistoryEventStatus.Done => "#ecfdf3",
        HistoryEventStatus.Cancelled => "#fef3f2",
        HistoryEventStatus.Confirmed => "#ecfdf3",
        HistoryEventStatus.Pending => "#fffaeb",
        HistoryEventStatus.New => "#eff8ff",
        HistoryEventStatus.Overdue => "#fef3f2",
        HistoryEventStatus.Cleared => "#ecfdf3",
        HistoryEventStatus.Bounced => "#fef3f2",
        HistoryEventStatus.LowStock => "#fffaeb",
        HistoryEventStatus.Normal => "#f1f5f6",
        HistoryEventStatus.Discharge => "#ecfdf3",
        HistoryEventStatus.CheckIn => "#eff8ff",
        _ => "#f1f5f6",
    };

    /// <summary>The tone's text color (the <c>--*</c> token that matches <see cref="StatusClass"/>).</summary>
    public static string StatusColor(HistoryEventStatus status) => status switch
    {
        HistoryEventStatus.Paid => "#027a48",
        HistoryEventStatus.Partial => "#b54708",
        HistoryEventStatus.Unpaid => "#d92d20",
        HistoryEventStatus.Done => "#027a48",
        HistoryEventStatus.Cancelled => "#d92d20",
        HistoryEventStatus.Confirmed => "#027a48",
        HistoryEventStatus.Pending => "#b54708",
        HistoryEventStatus.New => "#1570cd",
        HistoryEventStatus.Overdue => "#d92d20",
        HistoryEventStatus.Cleared => "#027a48",
        HistoryEventStatus.Bounced => "#d92d20",
        HistoryEventStatus.LowStock => "#b54708",
        HistoryEventStatus.Normal => "#667085",
        HistoryEventStatus.Discharge => "#027a48",
        HistoryEventStatus.CheckIn => "#1570cd",
        _ => "#667085",
    };

    // ── Day-group friendly label (the reference's jdateLabel) ────────────

    /// <summary>
    /// «امروز» / «دیروز» / «پریروز» / «۱۷ مهر ۱۴۰۵» — the reference's
    /// <c>jdateLabel</c>, derived with the Phase 04 day-arithmetic helper.
    /// </summary>
    public static string DayLabel(string jdate)
    {
        var diff = JalaliCalendar.Diff(JalaliCalendar.Today, jdate);
        return diff switch
        {
            0 => "امروز",
            1 => "دیروز",
            2 => "پریروز",
            _ => JalaliCalendar.Long(jdate),
        };
    }
}
