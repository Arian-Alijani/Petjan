using Animora.Desktop.Localization;
using Avalonia;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Inventory;

/// <summary>
/// Presentation adapters for the Inventory screen: the money formatter, the
/// product-thumbnail gradients, the category tone palettes and the status
/// colors — all transcribed from the reference (Animora-UI/assets/inventory.js
/// <c>formatFull</c>/<c>fmt</c>, the <c>.pthumb.(a..f)</c> gradients and the
/// <c>.cat-tag.tone-*</c> / <c>.tone-dot.tone-*</c> / <c>.status-dot.*</c>
/// rules in <c>inventory.css</c> + <c>styles.css</c>). Kept here so the
/// ViewModel and View stay free of hardcoded literals.
/// </summary>
public static class InventoryPresentation
{
    /// <summary>A solid fill brush from a hex string.</summary>
    public static IBrush Fill(string hex) => new SolidColorBrush(Color.Parse(hex));

    /// <summary>
    /// Mirrors the reference's <c>formatFull(n)</c> — a Persian-grouped integer
    /// (<c>n.toLocaleString('fa-IR')</c>), used for stock counts and prices.
    /// </summary>
    public static string Money(long value) => PersianNumberFormatter.FormatGrouped(value);

    /// <summary>
    /// The product-thumbnail fill, mirroring the reference's
    /// <c>.pthumb.(a..f)</c> two-stop 135° gradients (inventory.css). The
    /// gradient runs from the top-left to the bottom-right, so start = (0,0)
    /// and end = (1,1).
    /// </summary>
    public static IBrush ThumbBrush(string? tone)
    {
        var (from, to) = tone switch
        {
            "a" => ("#ece5fb", "#b8a2e6"),
            "b" => ("#fbe7d6", "#f0b593"),
            "c" => ("#fff2cc", "#fcd97a"),
            "d" => ("#fef3f2", "#ea8f8f"),
            "e" => ("#ecfdf3", "#7ac7b6"),
            "f" => ("#eff8ff", "#8fb5f0"),
            _ => ("#ece5fb", "#b8a2e6"),
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

    /// <summary>
    /// Category-tag colors, mirroring <c>.cat-tag.tone-*</c> (inventory.css):
    /// soft-bg fill + a darker readable label color.
    /// </summary>
    public static (IBrush Background, IBrush Foreground) CategoryTag(string tone) => tone switch
    {
        "teal" => (Fill("#f0fdfa"), Fill("#104a43")),
        "peach" => (Fill("#fdf1e7"), Fill("#a8623a")),
        "lilac" => (Fill("#f4f3ff"), Fill("#6c4bb8")),
        "amber" => (Fill("#fffaeb"), Fill("#8a5b18")),
        "blue" => (Fill("#eff8ff"), Fill("#345aa8")),
        "green" => (Fill("#ecfdf3"), Fill("#226b4d")),
        _ => (Fill("#f1f5f6"), Fill("#344054")),
    };

    /// <summary>
    /// Category icon-tile colors, mirroring <c>.cat-setting-row .ic.tone-*</c>
    /// (inventory.css) — soft-bg fill + the tone's 700-strength glyph color.
    /// </summary>
    public static (IBrush Background, IBrush Foreground) CategoryIconTile(string tone) => tone switch
    {
        "teal" => (Fill("#f0fdfa"), Fill("#0e7566")),
        "peach" => (Fill("#fdf1e7"), Fill("#a8623a")),
        "lilac" => (Fill("#f4f3ff"), Fill("#6c4bb8")),
        "amber" => (Fill("#fffaeb"), Fill("#8a5b18")),
        "blue" => (Fill("#eff8ff"), Fill("#345aa8")),
        "green" => (Fill("#ecfdf3"), Fill("#226b4d")),
        _ => (Fill("#f1f5f6"), Fill("#344054")),
    };

    /// <summary>
    /// The tone-picker dot swatches, mirroring <c>.tone-dot.tone-*</c>
    /// (inventory.css).
    /// </summary>
    public static IBrush ToneDot(string tone) => Fill(tone switch
    {
        "teal" => "#14b8a6",
        "peach" => "#e79b70",
        "lilac" => "#9b7bd8",
        "amber" => "#d6a52f",
        "blue" => "#6f9be8",
        "green" => "#4aa578",
        _ => "#14b8a6",
    });

    /// <summary>
    /// The category filter-chip dot color, mirroring <c>.pb-chip.tone-* .dot</c>
    /// (styles.css).
    /// </summary>
    public static IBrush ChipDot(string tone) => Fill(tone switch
    {
        "teal" => "#0d9488",
        "peach" => "#f0b593",
        "lilac" => "#a48afb",
        "amber" => "#b54708",
        "blue" => "#1570cd",
        "green" => "#027a48",
        _ => "#667085",
    });

    /// <summary>
    /// The status-dot text color, mirroring <c>.status-dot.ok/.warn/.low</c>
    /// (inventory.css).
    /// </summary>
    public static IBrush StatusForeground(string tone) => Fill(tone switch
    {
        "ok" => "#027a48",
        "warn" => "#b54708",
        "low" => "#d92d20",
        _ => "#027a48",
    });

    /// <summary>
    /// The status dot's own fill, mirroring <c>.status-dot(::before)</c>
    /// (inventory.css) — ok uses the success token, warn the amber, low the
    /// danger red.
    /// </summary>
    public static IBrush StatusDot(string tone) => Fill(tone switch
    {
        "ok" => "#2f8f6a",
        "warn" => "#c98a2b",
        "low" => "#d0403a",
        _ => "#2f8f6a",
    });

    /// <summary>
    /// The full stock-progress-bar brush, mirroring <c>.stockcell .bar</c>
    /// (bg-2 track) with <c>.stockcell .bar &gt; i(.low/.warn)</c> (brand-500 /
    /// danger / warn fill) at <paramref name="percent"/>% — as one gradient so
    /// the bar is fully data-bound. The reference's fill block <c>i</c> grows
    /// from the inline-start (right, RTL) edge.
    /// </summary>
    public static IBrush StockBarBrush(string status, int percent)
    {
        var (from, to) = status switch
        {
            "low" => ("#b54708", "#b54708"),   // .warn
            "out" => ("#d92d20", "#d92d20"),   // .low (danger)
            _ => ("#14b8a6", "#14b8a6"),       // --brand-500
        };

        var frac = Math.Clamp(percent / 100d, 0d, 1d);
        var fill = Color.Parse(from);
        // The reference has a single-colour fill (no gradient); the second
        // stop just repeats it so the bar reads as one solid band.
        var fillEnd = Color.Parse(to);
        var track = Color.Parse("#f1f5f6");

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(fill, 0),
                new GradientStop(fillEnd, frac),
                new GradientStop(track, frac),
                new GradientStop(track, 1),
            },
        };
    }

    /// <summary>
    /// The price-range text color, mirroring <c>.price-range</c> and the
    /// row's <c>priceClass</c> (out → danger, low → warn, else ink).
    /// </summary>
    public static IBrush PriceForeground(string status) => Fill(status switch
    {
        "out" => "#d92d20",
        "low" => "#b54708",
        _ => "#101828",
    });

    /// <summary>
    /// A history-timeline icon tile fill, mirroring <c>.hist-ic.in/out/adjust</c>
    /// (inventory.css) — ok-bg / danger-bg / warn-bg.
    /// </summary>
    public static IBrush HistoryTileBackground(string kind) => Fill(kind switch
    {
        "in" => "#ecfdf3",
        "out" => "#fef3f2",
        _ => "#fffaeb",
    });

    /// <summary>The history-timeline icon glyph color, mirroring <c>.hist-ic.in/out/adjust</c>.</summary>
    public static IBrush HistoryTileForeground(string kind) => Fill(kind switch
    {
        "in" => "#027a48",
        "out" => "#d92d20",
        _ => "#b54708",
    });

    /// <summary>The history movement-quantity color, mirroring <c>.hist-qty.in/out/adjust</c>.</summary>
    public static IBrush HistoryQuantityForeground(string kind) => Fill(kind switch
    {
        "in" => "#027a48",
        "out" => "#d92d20",
        _ => "#b54708",
    });

    /// <summary>
    /// The product-modal KPI icon-tile fill, mirroring <c>.inv-kpi .ic.&lt;tone&gt;</c>
    /// (teal → brand-50, danger → danger-bg, warn → warn-bg, info → info-bg).
    /// </summary>
    public static IBrush KpiTileBackground(string tone) => Fill(tone switch
    {
        "teal" => "#f0fdfa",
        "danger" => "#fef3f2",
        "warn" => "#fffaeb",
        "info" => "#eff8ff",
        "ok" => "#ecfdf3",
        _ => "#f1f5f6",
    });

    /// <summary>The product-modal KPI icon glyph color, mirroring <c>.inv-kpi .ic.&lt;tone&gt;</c>.</summary>
    public static IBrush KpiTileForeground(string tone) => Fill(tone switch
    {
        "teal" => "#0e7566",
        "danger" => "#d92d20",
        "warn" => "#b54708",
        "info" => "#1570cd",
        "ok" => "#027a48",
        _ => "#344054",
    });

    /// <summary>
    /// The new-product «کالای جدید» header thumbnail fill — the reference's
    /// inline <c>linear-gradient(135deg,var(--brand-400),var(--brand-600))</c>.
    /// </summary>
    public static IBrush NewProductThumbBrush()
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#2dd4bf"), 0),
                new GradientStop(Color.Parse("#0d9488"), 1),
            },
        };
    }

    /// <summary>
    /// A row's muted/branding background used by the modal body — the reference's
    /// <c>linear-gradient(180deg,#fff,#fef7f6)</c> for a low-stock variant card.
    /// </summary>
    public static IBrush LowStockRowBackground()
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse("#ffffff"), 0),
                new GradientStop(Color.Parse("#fef7f6"), 1),
            },
        };
    }
}
