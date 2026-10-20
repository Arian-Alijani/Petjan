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

    /// <summary>
    /// The signed amount cell text shared by every history surface:
    /// «+۱۲٬۰۰۰ ت» / «−۱۲٬۰۰۰ ت», or «—» when the row carries no money.
    /// </summary>
    public static string SignedAmount(long amount, int amountSign)
    {
        if (amount == 0 || amountSign == 0)
        {
            return "—";
        }

        var sign = amountSign < 0 ? "−" : "+";
        return sign + Money(amount) + " ت";
    }

    /// <summary>The signed-amount brush (inflow = ok, outflow = danger, none = muted).</summary>
    public static IBrush AmountBrush(int amountSign) =>
        Fill(amountSign < 0 ? DangerColor : amountSign > 0 ? OkColor : MutedColor);

    // ── Shared neutral tokens (mirror Resources/Theme/Colors.axaml) ──────

    /// <summary>Muted ink «--muted» (#667085) — placeholder amounts, secondary text.</summary>
    public const string MutedColor = "#667085";

    /// <summary>Inactive chip label ink (#475467).</summary>
    public const string ChipMutedInkColor = "#475467";

    /// <summary>Inactive chip border line (#e4e7ec).</summary>
    public const string ChipLineColor = "#e4e7ec";

    public const string OkColor = "#027a48";
    public const string OkBgColor = "#ecfdf3";
    public const string WarnColor = "#b54708";
    public const string WarnBgColor = "#fffaeb";
    public const string DangerColor = "#d92d20";
    public const string DangerBgColor = "#fef3f2";
    public const string InfoColor = "#1570cd";
    public const string InfoBgColor = "#eff8ff";
    public const string LilacColor = "#a48afb";
    public const string LilacBgColor = "#f4f3ff";
    public const string NeutralColor = "#667085";
    public const string NeutralBgColor = "#f1f5f6";
    public const string BrandColor = "#3a56d4";
    public const string BrandBgColor = "#eef2fe";
    public const string PeachColor = "#f0b593";
    public const string PeachBgColor = "#fdf1e7";

    /// <summary>Inactive chip border brush (shared by every history kind-chip row).</summary>
    public static IBrush ChipLineBrush => Fill(ChipLineColor);

    /// <summary>Inactive chip label brush (shared by every history kind-chip row).</summary>
    public static IBrush ChipMutedInkBrush => Fill(ChipMutedInkColor);

    /// <summary>Muted ink brush (placeholder amounts, «—» cells).</summary>
    public static IBrush MutedBrush => Fill(MutedColor);

    // ── Tone palette (the single authority for every chip/badge tint) ────

    /// <summary>The accent text colour of a <see cref="Tone"/> (the <c>--*</c> token).</summary>
    public static string ToneColor(Tone tone) => tone switch
    {
        Tone.Ok => OkColor,
        Tone.Warn => WarnColor,
        Tone.Danger => DangerColor,
        Tone.Info => InfoColor,
        Tone.Lilac => LilacColor,
        Tone.Brand => BrandColor,
        Tone.Peach => PeachColor,
        _ => NeutralColor,
    };

    /// <summary>The tinted surface colour of a <see cref="Tone"/> (the <c>--*-bg</c> token).</summary>
    public static string ToneBg(Tone tone) => tone switch
    {
        Tone.Ok => OkBgColor,
        Tone.Warn => WarnBgColor,
        Tone.Danger => DangerBgColor,
        Tone.Info => InfoBgColor,
        Tone.Lilac => LilacBgColor,
        Tone.Brand => BrandBgColor,
        Tone.Peach => PeachBgColor,
        _ => NeutralBgColor,
    };

    /// <summary>Accent brush of a <see cref="Tone"/>.</summary>
    public static IBrush ToneBrush(Tone tone) => Fill(ToneColor(tone));

    /// <summary>Tinted-surface brush of a <see cref="Tone"/>.</summary>
    public static IBrush ToneBgBrush(Tone tone) => Fill(ToneBg(tone));

    /// <summary>
    /// The <see cref="Tone"/> behind each status (the reference's <c>STATUS[s].cls</c>);
    /// <see cref="StatusColor"/>/<see cref="StatusBg"/> derive from it so every chip shares one palette.
    /// </summary>
    public static Tone StatusTone(HistoryEventStatus status) => status switch
    {
        HistoryEventStatus.Paid => Tone.Ok,
        HistoryEventStatus.Partial => Tone.Warn,
        HistoryEventStatus.Unpaid => Tone.Danger,
        HistoryEventStatus.Done => Tone.Ok,
        HistoryEventStatus.Cancelled => Tone.Danger,
        HistoryEventStatus.Confirmed => Tone.Ok,
        HistoryEventStatus.Pending => Tone.Warn,
        HistoryEventStatus.New => Tone.Info,
        HistoryEventStatus.Overdue => Tone.Danger,
        HistoryEventStatus.Cleared => Tone.Ok,
        HistoryEventStatus.Bounced => Tone.Danger,
        HistoryEventStatus.LowStock => Tone.Warn,
        HistoryEventStatus.Normal => Tone.Neutral,
        HistoryEventStatus.Discharge => Tone.Ok,
        HistoryEventStatus.CheckIn => Tone.Info,
        _ => Tone.Neutral,
    };

    /// <summary>The <see cref="Tone"/> behind each module kind's icon badge / category pill.</summary>
    public static Tone KindTone(HistoryEventKind kind) => kind switch
    {
        HistoryEventKind.Sale => Tone.Ok,
        HistoryEventKind.Appointment => Tone.Info,
        HistoryEventKind.Patient => Tone.Lilac,
        HistoryEventKind.Vaccine => Tone.Ok,
        HistoryEventKind.Expense => Tone.Danger,
        HistoryEventKind.Inventory => Tone.Warn,
        HistoryEventKind.Cheque => Tone.Neutral,
        HistoryEventKind.Boarding => Tone.Lilac,
        HistoryEventKind.Visit => Tone.Info,
        HistoryEventKind.Purchase => Tone.Warn,
        _ => Tone.Neutral,
    };

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
    public static string KindColor(HistoryEventKind kind) => ToneColor(KindTone(kind));

    /// <summary>The kind icon badge's background (the reference uses the matching <c>--*-bg</c> token).</summary>
    public static string KindBg(HistoryEventKind kind) => ToneBg(KindTone(kind));

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
    public static string StatusClass(HistoryEventStatus status) => StatusTone(status) switch
    {
        Tone.Ok => "ok",
        Tone.Warn => "warn",
        Tone.Danger => "danger",
        Tone.Info => "info",
        Tone.Lilac => "lilac",
        _ => "mute",
    };

    /// <summary>The tone's background fill (the <c>--*-bg</c> token that matches <see cref="StatusClass"/>).</summary>
    public static string StatusBg(HistoryEventStatus status) => ToneBg(StatusTone(status));

    /// <summary>The tone's text color (the <c>--*</c> token that matches <see cref="StatusClass"/>).</summary>
    public static string StatusColor(HistoryEventStatus status) => ToneColor(StatusTone(status));

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
