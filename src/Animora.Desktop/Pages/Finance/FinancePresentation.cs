using System;
using System.Globalization;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Avalonia;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Finance;

/// <summary>
/// Presentation adapters for the Finance workspace: the money / short-money /
/// decimal-percent / signed-percent formatters, the tone colors for the donut
/// legends, the category icon tiles, the rank badges, the kind tags and the
/// heatmap palette — all transcribed from the reference
/// (Animora-UI/assets/finance-page.js number helpers + the <c>.fin-*</c> /
/// <c>.svp-*</c> rules in finance.css and the shared <c>--fin-*</c> /
/// tone tokens). Kept here so the ViewModel and View stay free of literals.
/// </summary>
public static class FinancePresentation
{
    // ── finance.css :root page-scoped semantic pair ──────────────────────

    /// <summary>--fin-income (#0d9488).</summary>
    public const string IncomeColor = "#0d9488";

    /// <summary>--fin-income-bg (#e7f4f0).</summary>
    public const string IncomeBgColor = "#e7f4f0";

    /// <summary>--fin-expense (#d92d20).</summary>
    public const string ExpenseColor = "#d92d20";

    /// <summary>--fin-expense-bg (#fef3f2).</summary>
    public const string ExpenseBgColor = "#fef3f2";

    /// <summary>--fin-profit (#1570cd).</summary>
    public const string ProfitColor = "#1570cd";

    /// <summary>--fin-profit-bg (#eff8ff).</summary>
    public const string ProfitBgColor = "#eff8ff";

    /// <summary>--fin-grid (#eef2f0).</summary>
    public const string GridColor = "#eef2f0";

    /// <summary>--fin-track (#f0f4f2).</summary>
    public const string TrackColor = "#f0f4f2";

    // ── number formatting ───────────────────────────────────────────────

    /// <summary>Persian digits for a plain integer (the reference's <c>fa</c>).</summary>
    public static string Fa(long value) =>
        PersianNumberFormatter.ToPersianDigits(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Persian digits for a text value, preserving other characters (<c>fa(str)</c>).</summary>
    public static string FaText(string? text) => PersianNumberFormatter.ToPersianDigits(text ?? string.Empty);

    /// <summary>The reference's <c>money(n)</c> — a Persian-grouped integer.</summary>
    public static string Money(long value) => PersianNumberFormatter.FormatGrouped(value);

    /// <summary>
    /// The reference's <c>moneyShort</c>: «۱۲.۵ م» / «۱.۲ میلیارد» / «۸۵۰ هزار»
    /// (the JS keeps an ASCII dot between the digits — mirrored verbatim).
    /// </summary>
    public static string MoneyShort(long n)
    {
        if (n >= 1_000_000_000)
        {
            return Fa2(n, 1_000_000_000d) + " میلیارد";
        }

        if (n >= 1_000_000)
        {
            return Fa2(n, 1_000_000d) + " م";
        }

        if (n >= 1_000)
        {
            return Fa((long)Math.Round(n / 1_000d, MidpointRounding.AwayFromZero)) + " هزار";
        }

        return Fa(n);

        static string Fa2(long value, double unit)
        {
            var scaled = value / unit;
            var frac = value % (long)unit;
            var text = scaled.ToString(frac == 0 ? "0" : "0.#", CultureInfo.InvariantCulture);
            return PersianNumberFormatter.ToPersianDigits(text);
        }
    }

    /// <summary>
    /// The reference's <c>faDec(n, d)</c> — a fixed-decimal value rendered with
    /// a Persian decimal separator (٫), used on the KPI values (millions).
    /// </summary>
    public static string FaDec(double value, int digits) =>
        PersianNumberFormatter.ToPersianDigits(Fix(value, digits).Replace('.', '٫'));

    /// <summary>
    /// The reference's <c>signPct(v)</c> — a signed one-decimal percent with a
    /// Persian decimal separator and a plus/minus prefix (e.g. «+۱۲٫۵٪»).
    /// </summary>
    public static string SignPct(double value)
    {
        var s = Fix(Math.Abs(value), 1).Replace('.', '٫');
        return (value >= 0 ? "+" : "−") + PersianNumberFormatter.ToPersianDigits(s) + "٪";
    }

    private static string Fix(double value, int digits) =>
        value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>The reference's <c>fa(v) + '٪'</c> for a whole-number percent.</summary>
    public static string FaPercent(double value) => Fa((long)Math.Round(value, MidpointRounding.AwayFromZero)) + "٪";

    // ── brushes ─────────────────────────────────────────────────────────

    /// <summary>A solid fill brush from a hex string (shared via <see cref="BrushCache"/>).</summary>
    public static IBrush Fill(string hex) => BrushCache.Solid(hex);

    /// <summary>
    /// A translucent fill from a hex color + an 8-bit alpha, mirroring the
    /// reference's inline <c>background:${color}1a</c> (10%) pattern. The
    /// (colour, alpha) pair is shared via <see cref="BrushCache"/> — both come
    /// from the fixed palette, so the key space stays bounded
    /// (PERFORMANCE.md §15).
    /// </summary>
    public static IBrush FillAlpha(string hex, byte alpha) => BrushCache.Solid(hex, alpha);

    /// <summary>Blends <paramref name="hexA"/> with <paramref name="hexB"/> by weight (0..1) — mirrors <c>color-mix</c>.</summary>
    public static string Mix(string hexA, string hexB, double weightA)
    {
        var a = Color.Parse(hexA);
        var b = Color.Parse(hexB);
        byte M(byte x, byte y) => (byte)Math.Round((x * weightA) + (y * (1 - weightA)));
        return $"#{M(a.R, b.R):X2}{M(a.G, b.G):X2}{M(a.B, b.B):X2}";
    }

    /// <summary>The 135° hero gradient (linear-gradient(135deg, #1a5c56, #104a43)).</summary>
    public static IBrush HeroGradient() =>
        // Shared via GradientBrushCache — one fixed pair (PERFORMANCE.md §10).
        GradientBrushCache.LinearGradient("#1a5c56", "#104a43", 0, 0, 1, 1);

    /// <summary>
    /// The legend-row fill gradient (<c>linear-gradient(to left, c, c@25%)</c>),
    /// drawn at 15% opacity from the inline-start edge.
    /// </summary>
    public static IBrush LegendFill(string hex) =>
        // Shared via GradientBrushCache's alpha-aware overload: opaque `hex`
        // fading to `hex`@0x40. Both colours come from the fixed palette and
        // the alpha is 8-bit, so the key space stays bounded (§10/§15).
        GradientBrushCache.LinearGradient(hex, 0xFF, hex, 0x40, 1, 0, 0, 0);

    /// <summary>The donut area gradient (<c>var(--fin-income)</c> 26% → 0) filled under the revenue line.</summary>
    public static IBrush AreaGradient(string hex, double topOpacity)
    {
        // The top stop's alpha is derived from the (design-fixed) opacity; bake
        // it to an 8-bit value and let the alpha-aware gradient cache own the
        // instance (bounded keys: fixed palette × 8-bit alpha, §10/§15).
        var topAlpha = (byte)Math.Round(255 * Math.Clamp(topOpacity, 0d, 1d));
        return GradientBrushCache.LinearGradient(hex, topAlpha, hex, 0x00, 0, 0, 0, 1);
    }

    // ── tone maps ───────────────────────────────────────────────────────

    /// <summary>The <c>.fin-list-ic.&lt;tone&gt;</c> icon-tile background.</summary>
    public static IBrush ListIconBackground(string tone) => Fill(tone switch
    {
        "teal" => "#f0fdfa",
        "blue" => "#eff8ff",
        "lilac" => "#f4f3ff",
        "amber" => "#fffaeb",
        "peach" => "#fdf1e7",
        "mute" => "#eef2f0",
        "warn" => "#fffaeb",
        _ => "#f0fdfa",
    });

    /// <summary>The <c>.fin-list-ic.&lt;tone&gt;</c> icon glyph color.</summary>
    public static IBrush ListIconForeground(string tone) => Fill(tone switch
    {
        "teal" => "#0e7566",
        "blue" => "#1570cd",
        "lilac" => "#7b5cc4",
        "amber" => "#b54708",
        "peach" => "#d16a20",
        "mute" => "#536b68",
        "warn" => "#b54708",
        _ => "#0e7566",
    });

    /// <summary>The <c>.fin-insight .ic.&lt;tone&gt;</c> tile background.</summary>
    public static IBrush InsightIconBackground(string tone) => Fill(tone switch
    {
        "ok" => "#ecfdf3",
        "warn" => "#fffaeb",
        "info" => "#eff8ff",
        "lilac" => "#f4f3ff",
        _ => "#eff8ff",
    });

    /// <summary>The <c>.fin-insight .ic.&lt;tone&gt;</c> icon glyph color.</summary>
    public static IBrush InsightIconForeground(string tone) => Fill(tone switch
    {
        "ok" => "#027a48",
        "warn" => "#b54708",
        "info" => "#1570cd",
        "lilac" => "#7b5cc4",
        _ => "#1570cd",
    });

    /// <summary>The <c>.fin-highlight.&lt;tone&gt;::before</c> rail color.</summary>
    public static IBrush HighlightRail(string tone) => Fill(tone switch
    {
        "ok" => IncomeColor,
        "warn" => "#b54708",
        _ => ProfitColor,
    });

    /// <summary>The <c>.fin-highlight.&lt;tone&gt; .fin-hl-ic</c> tile background.</summary>
    public static IBrush HighlightIconBackground(string tone) => Fill(tone switch
    {
        "ok" => IncomeBgColor,
        "warn" => "#fffaeb",
        _ => ProfitBgColor,
    });

    /// <summary>The <c>.fin-highlight.&lt;tone&gt; .fin-hl-ic</c> icon color.</summary>
    public static IBrush HighlightIconForeground(string tone) => Fill(tone switch
    {
        "ok" => IncomeColor,
        "warn" => "#b54708",
        _ => ProfitColor,
    });

    /// <summary>The <c>.fin-mini.&lt;tone&gt;::before</c> rail color (default brand-500).</summary>
    public static IBrush MiniRail(string tone) => Fill(tone switch
    {
        "income" => IncomeColor,
        "expense" => ExpenseColor,
        "profit" => ProfitColor,
        _ => "#14b8a6",
    });

    /// <summary>The <c>.fin-mini-ic</c> tile background for a tone.</summary>
    public static IBrush MiniIconBackground(string tone) => Fill(tone switch
    {
        "income" => IncomeBgColor,
        "expense" => ExpenseBgColor,
        "profit" => ProfitBgColor,
        _ => "#f0fdfa",
    });

    /// <summary>The <c>.fin-mini-ic</c> icon glyph color for a tone.</summary>
    public static IBrush MiniIconForeground(string tone) => Fill(tone switch
    {
        "income" => IncomeColor,
        "expense" => ExpenseColor,
        "profit" => ProfitColor,
        _ => "#0e7566",
    });

    /// <summary>The <c>.fin-kpi-ic.(income|expense|profit)</c> tile background.</summary>
    public static IBrush KpiIconBackground(string tone) => Fill(tone switch
    {
        "income" => IncomeBgColor,
        "expense" => ExpenseBgColor,
        "profit" => ProfitBgColor,
        _ => "#f0fdfa",
    });

    /// <summary>The <c>.fin-kpi-ic.(income|expense|profit)</c> icon glyph color.</summary>
    public static IBrush KpiIconForeground(string tone) => Fill(tone switch
    {
        "income" => IncomeColor,
        "expense" => ExpenseColor,
        "profit" => ProfitColor,
        _ => "#0e7566",
    });

    /// <summary>The <c>.fin-kpi-delta.(good|bad)</c> pill background.</summary>
    public static IBrush DeltaBackground(string tone) => Fill(tone == "bad" ? "#fef3f2" : "#ecfdf3");

    /// <summary>The <c>.fin-kpi-delta.(good|bad)</c> pill foreground.</summary>
    public static IBrush DeltaForeground(string tone) => Fill(tone == "bad" ? "#d92d20" : "#027a48");

    /// <summary>The <c>.fin-kind-tag.(svc|prod)</c> background.</summary>
    public static IBrush KindTagBackground(string kind) => Fill(kind == "service" ? IncomeBgColor : "#eff8ff");

    /// <summary>The <c>.fin-kind-tag.(svc|prod)</c> foreground.</summary>
    public static IBrush KindTagForeground(string kind) => Fill(kind == "service" ? IncomeColor : "#1570cd");

    /// <summary>The <c>.fin-rank.rank-N</c> background.</summary>
    public static IBrush RankBackground(int rank) => Fill(rank switch
    {
        1 => "#fbeecb",
        2 => "#eef1f4",
        3 => "#f7e6d5",
        _ => "#f1f5f6",
    });

    /// <summary>The <c>.fin-rank.rank-N</c> foreground.</summary>
    public static IBrush RankForeground(int rank) => Fill(rank switch
    {
        1 => "#b7791f",
        2 => "#667085",
        3 => "#c05621",
        _ => "#667085",
    });

    /// <summary>The insight icon key from the reference's INSIGHTS row (<c>trendUp|alert|bulb|piggy</c>).</summary>
    public static string InsightIcon(string icon) => icon switch
    {
        "trendUp" => "IconTrendUp",
        "alert" => "IconAlert",
        "bulb" => "IconBulb",
        "piggy" => "IconPiggy",
        _ => "IconBulb",
    };

    /// <summary>
    /// The <c>.fin-head-chip.(good|bad)</c> pill background (reused by the profit
    /// gauge card's delta chip, finance.css <c>.fin-head-chip.good/.bad</c>).
    /// </summary>
    public static IBrush HeadChipBackground(string? tone) => Fill(
        tone == "good" ? "#ecfdf3" : tone == "bad" ? "#fef3f2" : "#f1f5f6");

    /// <summary>The <c>.fin-head-chip.(good|bad)</c> pill foreground.</summary>
    public static IBrush HeadChipForeground(string? tone) => Fill(
        tone == "good" ? "#027a48" : tone == "bad" ? "#d92d20" : "#667085");

    /// <summary>
    /// The icon key of a payment method (mirrors the reference's category/stat
    /// icon for the expense modal + table; both use the shared <c>Icons.card</c>
    /// for the payment-method family).
    /// </summary>
    public static string PaymentMethodIcon(string id) => id switch
    {
        "pos" => "IconCreditCard",
        "cash" => "IconCoins",
        "transfer" => "IconBank",
        "credit" => "IconCreditCard",
        _ => "IconCreditCard",
    };

    // ── heatmap ─────────────────────────────────────────────────────────

    private static readonly string[] HeatShades =
        ["#eaf2ef", "#c8e4dd", "#a5d3c6", "#6ec0ac", "#14b8a6", "#104a43"];

    /// <summary>The heatmap cell fill for an intensity 0..5.</summary>
    public static IBrush HeatFill(int intensity) =>
        Fill(HeatShades[Math.Clamp(intensity, 0, HeatShades.Length - 1)]);

    // The heatmap scale is a fixed, six-entry palette; resolve its brushes once
    // (each shared via BrushCache) instead of rebuilding the list per call
    // (PERFORMANCE.md §2/§10).
    private static readonly IReadOnlyList<IBrush> HeatShadesBrushes =
        Array.ConvertAll(HeatShades, Fill);

    /// <summary>The six legend swatches of the heatmap scale (کم → زیاد).</summary>
    public static IReadOnlyList<IBrush> HeatScale() => HeatShadesBrushes;

    /// <summary>Saturday-first weekday headers of the heatmap rows.</summary>
    public static IReadOnlyList<string> HeatDays { get; } =
        ["شنبه", "یک‌شنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنج‌شنبه", "جمعه"];
}
