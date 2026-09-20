using System.Collections.Generic;
using Animora.Contracts.Appointments;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Avalonia;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// Shared presentation mapping for the Appointments calendar. Every value here
/// is transcribed from the screen's own CSS/JS in the reference
/// (Animora-UI/assets/appointments.css + assets/appointments.js): the per-type
/// accent/background/foreground palette (.t-*), the status pill meta (STATUS),
/// the invoice/payment/visit meta (INV_META/PAY_META/VISIT_META), the pet
/// avatar gradient tones (.avatar-pet.a..f) and the doctor accents. Keeping the
/// hex values + brush construction here keeps them out of the data contracts
/// and lets the Views bind pre-resolved brushes without any color logic.
/// </summary>
public static class AppointmentsPresentation
{
    /// <summary>ASCII → Persian digits (delegates to Phase 04's formatter).</summary>
    public static string Fa(long value) => PersianNumberFormatter.ToPersianDigits(value);

    public static string Fa(string value) => PersianNumberFormatter.ToPersianDigits(value);

    // ── per-type palette (appointments.css `.t-*`) ────────────────────────────
    private static readonly Dictionary<string, (string Accent, string Bg, string Fg)> TypePalette = new()
    {
        ["visit"] = ("#0d9488", "#e6f4f0", "#125b52"),
        ["vaccine"] = ("#1570cd", "#e6eefb", "#2f5299"),
        ["dental"] = ("#8f74d1", "#ece6fa", "#5b3ea1"),
        ["surgery"] = ("#d92d20", "#fbe6e3", "#96291f"),
        ["groom"] = ("#e08a5c", "#fbead9", "#8a4a1e"),
        ["lab"] = ("#2f9fb0", "#e2f2f5", "#1d6672"),
        ["followup"] = ("#b54708", "#fbf0d9", "#8a5c14"),
    };

    private static (string Accent, string Bg, string Fg) Palette(string classKey) =>
        TypePalette.TryGetValue(classKey, out var p) ? p : ("#98a2b3", "#eef2f0", "#536b68");

    /// <summary>The accent brush for a type's CSS class key.</summary>
    public static IBrush TypeAccent(string classKey) => Brush(Palette(classKey).Accent);

    /// <summary>The background brush for a type's CSS class key.</summary>
    public static IBrush TypeBackground(string classKey) => Brush(Palette(classKey).Bg);

    /// <summary>The foreground (text) brush for a type's CSS class key.</summary>
    public static IBrush TypeForeground(string classKey) => Brush(Palette(classKey).Fg);

    /// <summary>The left accent border brush (same as the type accent).</summary>
    public static IBrush Accent(string hex) => Brush(hex);

    // ── status meta (appointments.js STATUS) ──────────────────────────────────
    public static (string Label, Tone Tone) Status(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Confirmed => ("تأیید شده", Tone.Info),
        AppointmentStatus.Pending => ("در انتظار تأیید", Tone.Warn),
        AppointmentStatus.Done => ("پذیرش شده", Tone.Ok),
        AppointmentStatus.Cancelled => ("لغو شده", Tone.Danger),
        _ => ("—", Tone.Neutral),
    };

    public static string StatusClassKey(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Pending => "pending",
        AppointmentStatus.Done => "done",
        AppointmentStatus.Cancelled => "cancel",
        _ => "confirmed",
    };

    // ── invoice meta (INV_META) ───────────────────────────────────────────────
    public static (string Label, Tone Tone) Invoice(AppointmentInvoiceState state) => state switch
    {
        AppointmentInvoiceState.Paid => ("پرداخت شده", Tone.Ok),
        AppointmentInvoiceState.Unpaid => ("پرداخت نشده", Tone.Danger),
        AppointmentInvoiceState.Partial => ("پرداخت جزئی", Tone.Warn),
        _ => ("بدون فاکتور", Tone.Neutral),
    };

    // ── payment meta (PAY_META) ───────────────────────────────────────────────
    public static (string Label, string IconKey) Payment(AppointmentPaymentMethod method) => method switch
    {
        AppointmentPaymentMethod.Cash => ("نقد", "IconWallet"),
        AppointmentPaymentMethod.Card => ("کارت", "IconIdCard"),
        AppointmentPaymentMethod.Online => ("آنلاین", "IconTrend"),
        _ => ("—", "IconWallet"),
    };

    // ── visit outcome meta (VISIT_META) ───────────────────────────────────────
    public static (string Label, Tone Tone) Visit(AppointmentVisitOutcome outcome) => outcome switch
    {
        AppointmentVisitOutcome.Complete => ("ویزیت انجام شد", Tone.Ok),
        AppointmentVisitOutcome.NoShow => ("عدم مراجعه", Tone.Danger),
        AppointmentVisitOutcome.Refunded => ("مسترد شد", Tone.Warn),
        _ => ("—", Tone.Neutral),
    };

    // ── pet avatar gradient tones (.avatar-pet.a..f) ─────────────────────────
    private static readonly Dictionary<string, (string From, string To)> AvatarTones = new()
    {
        ["a"] = ("#b8a2e6", "#8f74d1"),
        ["b"] = ("#f0b593", "#e08a5c"),
        ["c"] = ("#fcd97a", "#eab749"),
        ["d"] = ("#ea8f8f", "#d16060"),
        ["e"] = ("#7ac7b6", "#4aa591"),
        ["f"] = ("#8fb5f0", "#5c8ad6"),
    };

    /// <summary>The diagonal gradient brush for a pet avatar tone key (a..f).</summary>
    public static IBrush AvatarBrush(string? tone)
    {
        var key = tone is { Length: > 0 } ? tone : "a";
        if (!AvatarTones.TryGetValue(key, out var t))
        {
            t = AvatarTones["a"];
        }

        // Shared via GradientBrushCache — the six avatar tone pairs are a fixed
        // set, so this never allocates a brush per item (PERFORMANCE.md §10).
        return GradientBrushCache.LinearGradient(t.From, t.To, 0, 0, 1, 1);
    }

    // ── brush cache (hex → SolidColorBrush) ──────────────────────────────────
    // Delegates to the shared, process-wide BrushCache so callers that hit the
    // hot path (grid/queue/history builds) never re-allocate a brush per item.
    private static IBrush Brush(string hex) => BrushCache.Solid(hex);

    /// <summary>Minutes-from-midnight → "HH:mm" with Persian digits.</summary>
    public static string Time(int minutes)
    {
        var h = (minutes / 60).ToString("D2");
        var m = (minutes % 60).ToString("D2");
        return PersianNumberFormatter.ToPersianDigits($"{h}:{m}");
    }

    /// <summary>Minutes-from-midnight → "HH:mm – HH:mm" with Persian digits.</summary>
    public static string Range(int from, int to) => $"{Time(from)} – {Time(to)}";

    /// <summary>Clock hour label for the time-grid gutter, e.g. "۰۷:۰۰".</summary>
    public static string HourLabel(int minutes) => Time(minutes);
}
