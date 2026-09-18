using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Animora.Desktop.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Finance;

/// <summary>One tab of the toolbar (mirrors TABS in finance-page.js).</summary>
public sealed partial class FinanceTabViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public FinanceTabViewModel(string id, string label, string iconKey)
    {
        Id = id;
        Label = label;
        IconKey = iconKey;
    }

    /// <summary>overview | income | expense | profit.</summary>
    public string Id { get; }

    public string Label { get; }

    public string IconKey { get; }
}

/// <summary>One timeframe option of the range picker (mirrors D.RANGES).</summary>
public sealed partial class FinanceRangeViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public FinanceRangeViewModel(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public string Id { get; }

    public string Label { get; }
}

/// <summary>One sort option of the «پرفروش‌ترین» segmented control (PROD_SORTS).</summary>
public sealed partial class FinanceSortViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public FinanceSortViewModel(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public string Id { get; }

    public string Label { get; }
}

/// <summary>
/// A card header's trailing chip (finance.css <c>.fin-head-chip</c>): an icon
/// key plus the label text, with an optional ok/bad tone.
/// </summary>
public sealed class FinanceHeadChip
{
    public FinanceHeadChip(string iconKey, string text, string? tone = null)
    {
        IconKey = iconKey;
        Text = text;
        Tone = tone;
    }

    public string IconKey { get; }

    public string Text { get; }

    public string? Tone { get; }

    public IBrush Background => FinancePresentation.Fill(
        Tone == "good" ? "#ecfdf3" : Tone == "bad" ? "#fef3f2" : "#f1f5f6");

    public IBrush Foreground => FinancePresentation.Fill(
        Tone == "good" ? "#027a48" : Tone == "bad" ? "#d92d20" : "#667085");
}

/// <summary>
/// The Overview KPI hero card (finance.css <c>.fin-kpi</c>): the optional hero
/// teal fill, the tone icon tile, a signed delta pill, the big value+unit, the
/// label and the foot line.
/// </summary>
public sealed class FinanceKpiViewModel
{
    public FinanceKpiViewModel(
        bool isHero, string tone, string iconKey,
        string deltaIconKey, string deltaText, string deltaTone,
        string value, string unit, string? valueColor, string label, string foot)
    {
        IsHero = isHero;
        Tone = tone;
        IconKey = iconKey;
        DeltaIconKey = deltaIconKey;
        DeltaText = deltaText;
        DeltaTone = deltaTone;
        Value = value;
        Unit = unit;
        ValueColor = valueColor;
        Label = label;
        Foot = foot;
    }

    public string DeltaIconKey { get; }

    public bool IsHero { get; }

    public string Tone { get; }

    public string IconKey { get; }

    public string DeltaText { get; }

    public string DeltaTone { get; }

    public string Value { get; }

    public string Unit { get; }

    public string? ValueColor { get; }

    public string Label { get; }

    public string Foot { get; }

    public IBrush CardBackground => IsHero
        ? FinancePresentation.HeroGradient()
        : FinancePresentation.Fill("#ffffff");

    public IBrush CardBorder => IsHero ? FinancePresentation.Fill("#104a43") : FinancePresentation.Fill("#eef2f5");

    public IBrush ValueBrush => IsHero
        ? FinancePresentation.Fill("#ffffff")
        : string.IsNullOrEmpty(ValueColor) ? FinancePresentation.Fill("#101828") : FinancePresentation.Fill(ValueColor);

    public IBrush LabelBrush => FinancePresentation.Fill(IsHero ? "#c7e3de" : "#667085");

    public IBrush FootBrush => FinancePresentation.Fill(IsHero ? "#c7e3de" : "#98a2b3");

    public IBrush IconBackground => IsHero
        ? FinancePresentation.FillAlpha("#ffffff", 0x29)
        : FinancePresentation.KpiIconBackground(Tone);

    public IBrush IconForeground => IsHero
        ? FinancePresentation.Fill("#ffffff")
        : FinancePresentation.KpiIconForeground(Tone);

    public IBrush DeltaBackground => IsHero
        ? FinancePresentation.FillAlpha("#ffffff", 0x29)
        : FinancePresentation.DeltaBackground(DeltaTone);

    public IBrush DeltaForeground => IsHero
        ? FinancePresentation.Fill("#dff5ef")
        : FinancePresentation.DeltaForeground(DeltaTone);
}

/// <summary>
/// The per-tab mini stat card (finance.css <c>.fin-mini</c>): the toned left
/// rail, the tone icon tile, a small label + value + optional unit and an
/// optional delta pill.
/// </summary>
public sealed class FinanceMiniStatViewModel
{
    public FinanceMiniStatViewModel(
        string tone, string iconKey, string label, string value, string? unit,
        string? deltaText = null, string? deltaTone = null)
    {
        Tone = tone;
        IconKey = iconKey;
        Label = label;
        Value = value;
        Unit = unit;
        DeltaText = deltaText;
        DeltaTone = deltaTone ?? "good";
    }

    public string Tone { get; }

    public string IconKey { get; }

    public string Label { get; }

    public string Value { get; }

    public string? Unit { get; }

    public bool HasUnit => !string.IsNullOrEmpty(Unit);

    public string? DeltaText { get; }

    public bool HasDelta => !string.IsNullOrEmpty(DeltaText);

    public string DeltaTone { get; }

    public IBrush RailBrush => FinancePresentation.MiniRail(Tone);

    public IBrush IconBackground => FinancePresentation.MiniIconBackground(Tone);

    public IBrush IconForeground => FinancePresentation.MiniIconForeground(Tone);

    public IBrush DeltaBackground => FinancePresentation.DeltaBackground(DeltaTone);

    public IBrush DeltaForeground => FinancePresentation.DeltaForeground(DeltaTone);

    public string DeltaIconKey => DeltaTone == "bad" ? "IconTrendDown" : "IconArrowUpRight";
}

/// <summary>One row of a donut's legend (finance.css <c>.fin-leg-row</c>).</summary>
public sealed partial class FinanceLegendRowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public FinanceLegendRowViewModel(int index, string name, string value, string pct, string color, double fraction)
    {
        Index = index;
        Name = name;
        Value = value;
        Pct = pct;
        Color = color;
        Fraction = fraction;
    }

    public int Index { get; }

    public string Name { get; }

    public string Value { get; }

    public string Pct { get; }

    public string Color { get; }

    public double Fraction { get; }

    public IBrush Swatch => FinancePresentation.Fill(Color);

    public IBrush FillBrush => FinancePresentation.LegendFill(Color);
}

/// <summary>
/// An interactive donut card body (finance.css <c>.fin-donut-body</c>): the ring
/// geometry plus a legend with hover focus that swaps the center readout (the
/// reference's <c>bindDonuts</c> focus/blur, rendered statically at its base
/// value until hovered).
/// </summary>
public sealed partial class FinanceDonutViewModel : ObservableObject
{
    private readonly string _baseValue;
    private readonly string _baseLabel;

    public FinanceDonutViewModel(
        IReadOnlyList<Controls.DonutSlice> slices,
        string centerValue,
        string centerLabel,
        IReadOnlyList<FinanceLegendRowViewModel> legend)
    {
        Slices = slices;
        Legend = new ObservableCollection<FinanceLegendRowViewModel>(legend);
        _baseValue = centerValue;
        _baseLabel = centerLabel;
        CenterValue = centerValue;
        CenterLabel = centerLabel;
        CenterBrush = FinancePresentation.Fill("#101828");
    }

    public IReadOnlyList<Controls.DonutSlice> Slices { get; }

    public ObservableCollection<FinanceLegendRowViewModel> Legend { get; }

    [ObservableProperty]
    private string _centerValue;

    [ObservableProperty]
    private string _centerLabel;

    [ObservableProperty]
    private IBrush _centerBrush;

    /// <summary>Focus a segment (hover) — dims the rest and shows its readout, mirroring the reference.</summary>
    [RelayCommand]
    private void Focus(int index)
    {
        for (var i = 0; i < Legend.Count; i++)
        {
            Legend[i].IsActive = i == index;
        }

        if (index >= 0 && index < Legend.Count)
        {
            var row = Legend[index];
            CenterValue = row.Value;
            CenterLabel = row.Name + " · " + row.Pct;
            CenterBrush = FinancePresentation.Fill(row.Color);
        }
    }

    [RelayCommand]
    private void Blur()
    {
        foreach (var row in Legend)
        {
            row.IsActive = false;
        }

        CenterValue = _baseValue;
        CenterLabel = _baseLabel;
        CenterBrush = FinancePresentation.Fill("#101828");
    }
}

/// <summary>A category/expense list row (finance.css <c>.fin-list-row</c> + tone icon tile).</summary>
public sealed class FinanceCategoryRowViewModel
{
    public FinanceCategoryRowViewModel(
        string iconKey, string tone, string label, string value, string color, double fraction,
        string deltaText, string deltaTone)
    {
        IconKey = iconKey;
        Tone = tone;
        Label = label;
        Value = value;
        Color = color;
        Fraction = fraction;
        DeltaText = deltaText;
        DeltaTone = deltaTone;
    }

    public string IconKey { get; }

    public string Tone { get; }

    public string Label { get; }

    public string Value { get; }

    public string Color { get; }

    public double Fraction { get; }

    public string DeltaText { get; }

    public string DeltaTone { get; }

    public IBrush IconBackground => FinancePresentation.ListIconBackground(Tone);

    public IBrush IconForeground => FinancePresentation.ListIconForeground(Tone);

    public IBrush TrackFill => FinancePresentation.Fill(Color);

    public IBrush AmountBrush => FinancePresentation.Fill(FinancePresentation.ExpenseColor);

    public IBrush ChipBackground => FinancePresentation.DeltaBackground(DeltaTone);

    public IBrush ChipForeground => FinancePresentation.DeltaForeground(DeltaTone);

    public string ChipIconKey => DeltaTone == "bad" ? "IconTrendDown" : "IconArrowUpRight";
}

/// <summary>A top-product row (finance.css <c>.fin-list-row.prod-row</c> + rank + kind tag).</summary>
public sealed class FinanceProductRowViewModel
{
    public FinanceProductRowViewModel(
        int rank, string iconKey, string tone, string name, bool isService, string meta,
        double fraction, string color, string main, string sub)
    {
        Rank = rank;
        IconKey = iconKey;
        Tone = tone;
        Name = name;
        IsService = isService;
        Meta = meta;
        Fraction = fraction;
        Color = color;
        Main = main;
        Sub = sub;
    }

    public int Rank { get; }

    public string IconKey { get; }

    public string Tone { get; }

    public string Name { get; }

    public bool IsService { get; }

    public string KindLabel => IsService ? "خدمت" : "کالا";

    public string KindKey => IsService ? "service" : "product";

    public string Meta { get; }

    public double Fraction { get; }

    public string Color { get; }

    public string Main { get; }

    public string Sub { get; }

    public string RankText => FinancePresentation.Fa(Rank);

    public bool IsTopRank => Rank <= 3;

    public IBrush RankBackground => FinancePresentation.RankBackground(Rank);

    public IBrush RankForeground => FinancePresentation.RankForeground(Rank);

    public IBrush IconBackground => FinancePresentation.ListIconBackground(Tone);

    public IBrush IconForeground => FinancePresentation.ListIconForeground(Tone);

    public IBrush TrackFill => FinancePresentation.Fill(Color);

    public IBrush MainBrush => FinancePresentation.Fill(Color);

    public IBrush KindBackground => FinancePresentation.KindTagBackground(KindKey);

    public IBrush KindForeground => FinancePresentation.KindTagForeground(KindKey);
}

/// <summary>One side of the «خدمات در برابر کالا» card (finance.css <c>.svp-item</c>).</summary>
public sealed class FinanceServiceSplitViewModel
{
    public FinanceServiceSplitViewModel(string label, string iconKey, string color, string share, string value, string qty, string margin, double fraction)
    {
        Label = label;
        IconKey = iconKey;
        Color = color;
        Share = share;
        Value = value;
        Qty = qty;
        Margin = margin;
        Fraction = fraction;
    }

    public string Label { get; }

    public string IconKey { get; }

    public string Color { get; }

    public string Share { get; }

    /// <summary>This bucket's share of combined revenue, 0..1 (drives the split bar).</summary>
    public double Fraction { get; }

    public string Value { get; }

    public string Qty { get; }

    public string Margin { get; }

    public IBrush IconBackground => FinancePresentation.FillAlpha(Color, 0x1a);

    public IBrush IconForeground => FinancePresentation.Fill(Color);

    public IBrush ShareBrush => FinancePresentation.Fill(Color);
}

/// <summary>A species horizontal bar (finance.css <c>.fin-hbar</c>).</summary>
public sealed class FinanceSpeciesViewModel
{
    public FinanceSpeciesViewModel(string label, string value, string color, double fraction)
    {
        Label = label;
        Value = value;
        Color = color;
        Fraction = fraction;
    }

    public string Label { get; }

    public string Value { get; }

    public string Color { get; }

    public double Fraction { get; }

    public IBrush TrackFill => FinancePresentation.Fill(Color);
}

/// <summary>A channel-performance row of the Income tab (finance.css <c>.fin-list-row</c>, color-tinted icon).</summary>
public sealed class FinanceChannelBarViewModel
{
    public FinanceChannelBarViewModel(string iconKey, string color, string label, string share, double fraction, string value)
    {
        IconKey = iconKey;
        Color = color;
        Label = label;
        Share = share;
        Fraction = fraction;
        Value = value;
    }

    public string IconKey { get; }

    public string Color { get; }

    public string Label { get; }

    public string Share { get; }

    public double Fraction { get; }

    public string Value { get; }

    public IBrush IconBackground => FinancePresentation.FillAlpha(Color, 0x1a);

    public IBrush IconForeground => FinancePresentation.Fill(Color);

    public IBrush TrackFill => FinancePresentation.Fill(Color);

    public IBrush AmountBrush => FinancePresentation.Fill(FinancePresentation.IncomeColor);
}

/// <summary>A heatmap cell (finance.css <c>.fin-heatmap .cell</c>).</summary>
public sealed class FinanceHeatCellViewModel
{
    public FinanceHeatCellViewModel(int intensity, string tooltip)
    {
        Intensity = intensity;
        Tooltip = tooltip;
    }

    public int Intensity { get; }

    public string Tooltip { get; }

    public IBrush Fill => FinancePresentation.HeatFill(Intensity);
}

/// <summary>One heatmap row (a weekday label + its 12 hour cells).</summary>
public sealed class FinanceHeatRowViewModel
{
    public FinanceHeatRowViewModel(string dayLabel, IReadOnlyList<FinanceHeatCellViewModel> cells)
    {
        DayLabel = dayLabel;
        Cells = cells;
    }

    public string DayLabel { get; }

    public IReadOnlyList<FinanceHeatCellViewModel> Cells { get; }
}

/// <summary>An insight card (finance.css <c>.fin-insight</c>).</summary>
public sealed class FinanceInsightViewModel
{
    public FinanceInsightViewModel(string iconKey, string tone, string text)
    {
        IconKey = iconKey;
        Tone = tone;
        Text = text;
    }

    public string IconKey { get; }

    public string Tone { get; }

    public string Text { get; }

    public IBrush IconBackground => FinancePresentation.InsightIconBackground(Tone);

    public IBrush IconForeground => FinancePresentation.InsightIconForeground(Tone);
}

/// <summary>The «کشف کلیدی» highlight bar (finance.css <c>.fin-highlight</c>) with bold spans.</summary>
public sealed class FinanceHighlightViewModel
{
    public FinanceHighlightViewModel(string tone, string iconKey, string label, IReadOnlyList<RichSpan> spans)
    {
        Tone = tone;
        IconKey = iconKey;
        Label = label;
        Spans = spans;
    }

    public string Tone { get; }

    public string IconKey { get; }

    public string Label { get; }

    public IReadOnlyList<RichSpan> Spans { get; }

    public IBrush RailBrush => FinancePresentation.HighlightRail(Tone);

    public IBrush IconBackground => FinancePresentation.HighlightIconBackground(Tone);

    public IBrush IconForeground => FinancePresentation.HighlightIconForeground(Tone);
}
