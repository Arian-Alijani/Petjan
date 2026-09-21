using System.Globalization;
using Animora.Contracts.Sales;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Avalonia;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// Presentation adapters for the Sale/POS screen: money formatting, the
/// avatar-pet tone palette, the invoice-state/label maps and the status-colour
/// helpers — all transcribed from the reference
/// (<c>Animora-UI/assets/sale-page.js</c> + <c>assets/sale.css</c>). Kept here
/// so the ViewModel and View stay free of hardcoded literals.
/// </summary>
public static class SalePresentation
{
    /// <summary>
    /// The reference's <c>money(n)</c> — a Persian-grouped integer (e.g.
    /// 570000 → «۵۷۰٬۰۰۰»).
    /// </summary>
    public static string Money(long value) => PersianNumberFormatter.FormatGrouped(value);

    /// <summary>Persian digits for a plain integer (the reference's <c>toFa</c>).</summary>
    public static string Fa(long value) =>
        PersianNumberFormatter.ToPersianDigits(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// The invoice-state Persian label, mirroring <c>renderInvoiceContent</c>'s
    /// <c>statusLabel</c> map.
    /// </summary>
    public static string StateLabel(InvoiceState state) => state switch
    {
        InvoiceState.Draft => "پیش‌نویس",
        InvoiceState.Partial => "تسویه جزئی",
        InvoiceState.Settled => "تسویه کامل",
        InvoiceState.Void => "ابطال شده",
        InvoiceState.Refunded => "مسترد شده",
        InvoiceState.Deleted => "حذف شده",
        _ => "—",
    };

    /// <summary>
    /// The invoice state's tinted-pill surface, mirroring the reference's
    /// <c>.sw-item-dot.(draft|partial|settled|void|refunded)</c> and
    /// <c>.sw-inv-status.*</c> backgrounds — mapped 1:1 to Phase 02 tokens.
    /// </summary>
    public static IBrush StateDotBrush(InvoiceState state) => state switch
    {
        InvoiceState.Draft => Resolve("InfoBrush", "#1570cd"),
        InvoiceState.Partial => Resolve("WarnBrush", "#b54708"),
        InvoiceState.Settled => Resolve("OkBrush", "#027a48"),
        InvoiceState.Void => Resolve("DangerBrush", "#d92d20"),
        InvoiceState.Refunded => Resolve("LilacBrush", "#a48afb"),
        InvoiceState.Deleted => Resolve("Muted2Brush", "#98a2b3"),
        _ => Resolve("Muted2Brush", "#98a2b3"),
    };

    /// <summary>
    /// The avatar-pet tile fill for a tone key, mirroring the reference's
    /// <c>.avatar-pet.(a..f)</c> two-stop 135° gradients (styles.css).
    /// </summary>
    public static IBrush AvatarBrush(string? tone)
    {
        var (from, to) = tone switch
        {
            "a" => ("#b8a2e6", "#8f74d1"),
            "b" => ("#f0b593", "#e08a5c"),
            "c" => ("#fcd97a", "#eab749"),
            "d" => ("#ea8f8f", "#d16060"),
            "e" => ("#7ac7b6", "#4aa591"),
            "f" => ("#8fb5f0", "#5c8ad6"),
            _ => ("#f1f5f6", "#f1f5f6"),
        };

        // Shared via GradientBrushCache — six fixed tone pairs, so this never
        // allocates a brush per item (PERFORMANCE.md §10).
        return GradientBrushCache.LinearGradient(from, to, 0, 0, 1, 1);
    }

    /// <summary>The placeholder-avatar glyph colour (guest / no pet).</summary>
    public static IBrush PlaceholderAvatarBrush() => Resolve("Bg2Brush", "#f1f5f6");

    /// <summary>The guest-avatar glyph colour.</summary>
    public static IBrush PlaceholderAvatarForeground() => Resolve("Muted2Brush", "#98a2b3");

    /// <summary>
    /// The line-kind tag tone, mirroring the reference's
    /// <c>.sw-line .tag</c> (brand service) vs <c>.tag.item</c> (lilac item).
    /// </summary>
    public static (IBrush Background, IBrush Foreground) LineTagBrushes(InvoiceLineKind kind, bool isCustom)
    {
        if (isCustom)
        {
            return (Resolve("WarnBgBrush", "#fffaeb"), Resolve("WarnBrush", "#b54708"));
        }

        return kind == InvoiceLineKind.Service
            ? (Resolve("Brand50Brush", "#f0fdfa"), Resolve("Brand700Brush", "#0e7566"))
            : (Resolve("LilacBgBrush", "#f4f3ff"), Resolve("LilacBrush", "#a48afb"));
    }

    /// <summary>
    /// The payment-method chip tone, mirroring the reference's
    /// <c>.sw-history-item .m.(cash|card|cheque|credit)</c> palette.
    /// </summary>
    public static (IBrush Background, IBrush Foreground) MethodBrushes(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => (Resolve("OkBgBrush", "#ecfdf3"), Resolve("OkBrush", "#027a48")),
        PaymentMethod.Card => (Resolve("InfoBgBrush", "#eff8ff"), Resolve("InfoBrush", "#1570cd")),
        PaymentMethod.Cheque => (Resolve("WarnBgBrush", "#fffaeb"), Resolve("WarnBrush", "#b54708")),
        PaymentMethod.Credit => (Resolve("LilacBgBrush", "#f4f3ff"), Resolve("LilacBrush", "#a48afb")),
        _ => (Resolve("Bg2Brush", "#f1f5f6"), Resolve("MutedBrush", "#667085")),
    };

    /// <summary>The invoice-state → Phase 02 <see cref="Tone"/>, for the status pill.</summary>
    public static Tone StateTone(InvoiceState state) => state switch
    {
        InvoiceState.Draft => Tone.Info,
        InvoiceState.Partial => Tone.Warn,
        InvoiceState.Settled => Tone.Ok,
        InvoiceState.Void => Tone.Danger,
        InvoiceState.Refunded => Tone.Lilac,
        InvoiceState.Deleted => Tone.Neutral,
        _ => Tone.Neutral,
    };

    /// <summary>
    /// The confirm-dialog icon tile tone, mirroring the reference's
    /// <c>.sw-confirm-ico.(danger|warn|brand|neutral)</c> palette.
    /// </summary>
    public static (IBrush Background, IBrush Foreground) ConfirmIco(string tone) => tone switch
    {
        "danger" => (Resolve("DangerBgBrush", "#fef3f2"), Resolve("DangerBrush", "#d92d20")),
        "warn" => (Resolve("WarnBgBrush", "#fffaeb"), Resolve("WarnBrush", "#b54708")),
        "brand" => (Resolve("Brand50Brush", "#f0fdfa"), Resolve("Brand700Brush", "#0e7566")),
        _ => (Resolve("Bg2Brush", "#f1f5f6"), Resolve("MutedBrush", "#667085")),
    };

    /// <summary>The payment-method → catalog icon key (mirrors <c>renderPayment</c>'s <c>ic</c>).</summary>
    public static string MethodIconKey(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "IconWallet",
        PaymentMethod.Card => "IconCreditCard",
        PaymentMethod.Cheque => "IconCheque",
        PaymentMethod.Credit => "IconClock",
        _ => "IconCopy",
    };

    /// <summary>The reference's <c>M[method]</c> Persian label map.</summary>
    public static string MethodLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "نقدی",
        PaymentMethod.Card => "کارت",
        PaymentMethod.Cheque => "چک",
        PaymentMethod.Credit => "نسیه",
        PaymentMethod.Mixed => "ترکیبی",
        _ => "—",
    };

    /// <summary>
    /// The smart payment-preview band palette, mirroring the reference's
    /// <c>.sw-pay-preview.(neutral|success|warn|partial)</c> surfaces + icon tiles
    /// (sale.css). Returns (background, foreground, ico-background, ico-foreground).
    /// </summary>
    public static (IBrush Background, IBrush Foreground, IBrush IcoBackground, IBrush IcoForeground)
        PreviewBrushes(string tone) => tone switch
    {
        "success" => (Resolve("OkBgBrush", "#ecfdf3"), Resolve("OkBrush", "#027a48"),
                      Resolve("OkBrush", "#027a48"), Brushes.White),
        "warn" => (Resolve("DangerBgBrush", "#fef3f2"), Resolve("Ink2Brush", "#344054"),
                   Resolve("DangerBrush", "#d92d20"), Brushes.White),
        "partial" => (Resolve("WarnBgBrush", "#fffaeb"), Resolve("Ink2Brush", "#344054"),
                      Resolve("WarnBrush", "#b54708"), Brushes.White),
        _ => (Resolve("Bg2Brush", "#f1f5f6"), Resolve("Ink2Brush", "#344054"),
              Resolve("InfoBgBrush", "#eff8ff"), Resolve("InfoBrush", "#1570cd")),
    };

    /// <summary>The preview band icon, mirroring the reference's per-tone glyph.</summary>
    public static string PreviewIconKey(string tone) => tone switch
    {
        "success" => "IconCheck",
        "warn" => "IconAlert",
        "partial" => "IconClock",
        _ => "IconInfo",
    };

    private static IBrush Resolve(string key, string fallback) =>
        BrushCache.Solid(fallback);
}
