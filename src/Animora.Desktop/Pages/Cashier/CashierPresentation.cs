using Animora.Contracts.Cashier;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Avalonia;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Cashier;

/// <summary>
/// Presentation adapters for the Cashier screen: money/fa formatting, the
/// invoice-state tone/label maps, the line-tag palette and the stat-card tones —
/// all transcribed from the reference (<c>Animora-UI/assets/cashier.js</c> +
/// <c>assets/styles.css</c>). Kept here so the ViewModel and View stay free of
/// hardcoded literals.
/// </summary>
public static class CashierPresentation
{
    /// <summary>
    /// The reference's <c>money(n)</c> — a Persian-grouped integer (e.g.
    /// 570000 → «۵۷۰٬۰۰۰»).
    /// </summary>
    public static string Money(long value) => PersianNumberFormatter.FormatGrouped(value);

    /// <summary>Persian digits for a plain integer (the reference's <c>toFa</c>).</summary>
    public static string Fa(long value) =>
        PersianNumberFormatter.ToPersianDigits(value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>The cashier state's Phase 02 <see cref="Tone"/>, for the status pill.</summary>
    public static Tone StateTone(CashierInvoiceState state) => state switch
    {
        CashierInvoiceState.Draft => Tone.Info,
        CashierInvoiceState.Partial => Tone.Warn,
        CashierInvoiceState.Settled => Tone.Ok,
        CashierInvoiceState.Void => Tone.Danger,
        _ => Tone.Neutral,
    };

    /// <summary>
    /// The stat icon tile tone, mirroring the reference's <c>.stat-icon.(warn|
    /// ok|info|lilac)</c> surfaces — mapped 1:1 to Phase 02 tokens.
    /// </summary>
    public static Tone StatTone(string tone) => tone switch
    {
        "warn" => Tone.Warn,
        "ok" => Tone.Ok,
        "info" => Tone.Info,
        "lilac" => Tone.Lilac,
        "danger" => Tone.Danger,
        _ => Tone.Brand,
    };

    /// <summary>
    /// The stat icon tile's fill/foreground, mirroring the reference's
    /// <c>.stat-icon.(warn|ok|info|lilac)</c> palette (border stays brand-100).
    /// </summary>
    public static (IBrush Background, IBrush Foreground) StatIconBrushes(string tone) => tone switch
    {
        "warn" => (Resolve("WarnBgBrush", "#fffaeb"), Resolve("WarnBrush", "#b54708")),
        "ok" => (Resolve("OkBgBrush", "#ecfdf3"), Resolve("OkBrush", "#027a48")),
        "info" => (Resolve("InfoBgBrush", "#eff8ff"), Resolve("InfoBrush", "#1570cd")),
        "lilac" => (Resolve("LilacBgBrush", "#f4f3ff"), Resolve("LilacBrush", "#7b5cc4")),
        "danger" => (Resolve("DangerBgBrush", "#fef3f2"), Resolve("DangerBrush", "#d92d20")),
        _ => (Resolve("Brand50Brush", "#f0fdfa"), Resolve("Brand600Brush", "#0d9488")),
    };

    /// <summary>
    /// The line-kind tag tone, mirroring the reference's <c>.cs-line .tag</c>
    /// (brand service) vs <c>.tag.item</c> (lilac item).
    /// </summary>
    public static (IBrush Background, IBrush Foreground) LineTagBrushes(CashierLineKind kind) =>
        kind == CashierLineKind.Service
            ? (Resolve("Brand50Brush", "#f0fdfa"), Resolve("Brand700Brush", "#0e7566"))
            : (Resolve("LilacBgBrush", "#f4f3ff"), Resolve("LilacBrush", "#7b5cc4"));

    /// <summary>The line-kind tag label («خدمت» / «کالا»).</summary>
    public static string LineKindLabel(CashierLineKind kind) =>
        kind == CashierLineKind.Service ? "خدمت" : "کالا";

    /// <summary>The payment-method chip tone, mirroring <c>.cs-pay-history .item .m</c>.</summary>
    public static (IBrush Background, IBrush Foreground) MethodBrushes(CashierPaymentMethod method) => method switch
    {
        CashierPaymentMethod.Cash => (Resolve("OkBgBrush", "#ecfdf3"), Resolve("OkBrush", "#027a48")),
        CashierPaymentMethod.Card => (Resolve("InfoBgBrush", "#eff8ff"), Resolve("InfoBrush", "#1570cd")),
        CashierPaymentMethod.Cheque => (Resolve("WarnBgBrush", "#fffaeb"), Resolve("WarnBrush", "#b54708")),
        CashierPaymentMethod.Credit => (Resolve("LilacBgBrush", "#f4f3ff"), Resolve("LilacBrush", "#7b5cc4")),
        _ => (Resolve("Bg2Brush", "#f1f5f6"), Resolve("MutedBrush", "#667085")),
    };

    private static IBrush Resolve(string key, string fallback) =>
        new SolidColorBrush(Color.Parse(fallback));
}
