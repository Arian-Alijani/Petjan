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

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse(from), 0),
                new GradientStop(Color.Parse(to), 1),
            },
        };
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
        _ => Tone.Neutral,
    };

    private static IBrush Resolve(string key, string fallback) =>
        new SolidColorBrush(Color.Parse(fallback));
}
