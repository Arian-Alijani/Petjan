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

    // ── per-type colours ──────────────────────────────────────────────────────
    // Every AppointmentType carries its own Accent / Background / Foreground (assigned once by the
    // service layer), so brushes are resolved from the type itself — never from a fixed class-key
    // table, which left every non-reference type on the gray fallback.

    /// <summary>The accent brush of a type (cached, no per-render allocation).</summary>
    public static IBrush TypeAccent(AppointmentType type) => Brush(type.Accent);

    /// <summary>The tinted background brush of a type; derived from the accent when the type has none.</summary>
    public static IBrush TypeBackground(AppointmentType type) =>
        Brush(string.IsNullOrWhiteSpace(type.Background) ? ColorMath.Tint(type.Accent) : type.Background);

    /// <summary>The text brush of a type; derived from the accent when the type has none.</summary>
    public static IBrush TypeForeground(AppointmentType type) =>
        Brush(string.IsNullOrWhiteSpace(type.Foreground) ? ColorMath.Shade(type.Accent) : type.Foreground);

    /// <summary>Brush for an arbitrary accent hex (cached).</summary>
    public static IBrush AccentFromHex(string hex) => Brush(hex);

    /// <summary>Light tint brush derived from an accent hex, for backgrounds behind accent-coloured text.</summary>
    public static IBrush TintFromAccent(string hex) => Brush(ColorMath.Tint(hex));

    /// <summary>Very light wash of an accent (selected filter chip surface).</summary>
    public static IBrush SoftFromAccent(string hex) => Brush(ColorMath.Soft(hex));

    /// <summary>Muted outline of an accent for a selected filter chip.</summary>
    public static IBrush OutlineFromAccent(string hex) => Brush(ColorMath.Outline(hex));

    /// <summary>Dark shade brush derived from an accent hex, readable on <see cref="TintFromAccent"/>.</summary>
    public static IBrush ShadeFromAccent(string hex) => Brush(ColorMath.Shade(hex));

    private static class ColorMath
    {
        public static string Tint(string hex) => Mix(hex, 255, 0.88);

        public static string Shade(string hex) => Mix(hex, 0, 0.55);

        public static string Soft(string hex) => Mix(hex, 255, 0.91);

        public static string Outline(string hex) => Mix(hex, 255, 0.58);

        private static string Mix(string hex, int target, double amount)
        {
            if (!Color.TryParse(hex, out var c))
            {
                c = Color.Parse("#5A67D8");
            }

            int M(byte v) => (int)System.Math.Round(v + (target - v) * amount);
            return $"#{M(c.R):X2}{M(c.G):X2}{M(c.B):X2}";
        }
    }

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

    /// <summary>Label + tone for the full 7-state lifecycle (A04). Check-in is "admitted"; no-show never is.</summary>
    public static (string Label, Tone Tone) Lifecycle(AppointmentLifecycle state) => state switch
    {
        AppointmentLifecycle.Scheduled => ("در انتظار تأیید", Tone.Warn),
        AppointmentLifecycle.Confirmed => ("تأیید شده", Tone.Info),
        AppointmentLifecycle.CheckedIn => ("پذیرش شده", Tone.Ok),
        AppointmentLifecycle.InVisit => ("در حال ویزیت", Tone.Brand),
        AppointmentLifecycle.Completed => ("ویزیت انجام شد", Tone.Ok),
        AppointmentLifecycle.NoShow => ("عدم مراجعه", Tone.Danger),
        AppointmentLifecycle.Cancelled => ("لغو شده", Tone.Danger),
        _ => ("—", Tone.Neutral),
    };

    public static string LifecycleClassKey(AppointmentLifecycle state) => state switch
    {
        AppointmentLifecycle.Scheduled => "pending",
        AppointmentLifecycle.Confirmed => "confirmed",
        AppointmentLifecycle.CheckedIn => "checkedin",
        AppointmentLifecycle.InVisit => "invisit",
        AppointmentLifecycle.Completed => "done",
        AppointmentLifecycle.NoShow => "noshow",
        _ => "cancel",
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
