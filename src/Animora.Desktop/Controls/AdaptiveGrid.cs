using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace Animora.Desktop.Controls;

/// <summary>
/// Reflowing grid driven by the width actually given to the panel (i.e. the content area after the sidebar),
/// not by the window size: as many columns as keep each cell ≥ <see cref="MinItemWidth"/> (at most
/// <see cref="MaxColumns"/>), wrapping onto further rows. Cell widths are equal, or proportional to
/// <see cref="Weights"/> when all items fit on one row. Row height follows the tallest cell.
/// </summary>
public sealed class AdaptiveGrid : Panel
{
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<AdaptiveGrid, double>(nameof(MinItemWidth), 220);
    public static readonly StyledProperty<int> MaxColumnsProperty =
        AvaloniaProperty.Register<AdaptiveGrid, int>(nameof(MaxColumns), 4);
    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<AdaptiveGrid, double>(nameof(ColumnSpacing), 0);
    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<AdaptiveGrid, double>(nameof(RowSpacing), 0);
    public static readonly StyledProperty<string?> WeightsProperty =
        AvaloniaProperty.Register<AdaptiveGrid, string?>(nameof(Weights));

    static AdaptiveGrid()
    {
        AffectsMeasure<AdaptiveGrid>(MinItemWidthProperty, MaxColumnsProperty, ColumnSpacingProperty, RowSpacingProperty, WeightsProperty);
    }

    public double MinItemWidth { get => GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
    public int MaxColumns { get => GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }
    public double ColumnSpacing { get => GetValue(ColumnSpacingProperty); set => SetValue(ColumnSpacingProperty, value); }
    public double RowSpacing { get => GetValue(RowSpacingProperty); set => SetValue(RowSpacingProperty, value); }
    /// <summary>Comma-separated invariant-culture weights, e.g. "1.3,1".</summary>
    public string? Weights { get => GetValue(WeightsProperty); set => SetValue(WeightsProperty, value); }

    private IReadOnlyList<Control> Items()
    {
        var items = new List<Control>(Children.Count);
        foreach (var c in Children) if (c.IsVisible) items.Add(c);
        return items;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var items = Items();
        if (items.Count == 0) return default;
        var columns = AdaptiveGridMath.Columns(availableSize.Width, ColumnSpacing, MinItemWidth, MaxColumns, items.Count);
        var width = double.IsFinite(availableSize.Width)
            ? availableSize.Width
            : columns * MinItemWidth + (columns - 1) * ColumnSpacing;
        var widths = AdaptiveGridMath.CellWidths(width, ColumnSpacing, columns, items.Count, AdaptiveGridMath.ParseWeights(Weights));
        double total = 0;
        for (var row = 0; row * columns < items.Count; row++)
        {
            double rowHeight = 0;
            for (var col = 0; col < columns && row * columns + col < items.Count; col++)
            {
                var item = items[row * columns + col];
                item.Measure(new Size(widths[col], double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, item.DesiredSize.Height);
            }
            total += rowHeight + (row > 0 ? RowSpacing : 0);
        }
        return new Size(width, total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var items = Items();
        if (items.Count == 0) return finalSize;
        var columns = AdaptiveGridMath.Columns(finalSize.Width, ColumnSpacing, MinItemWidth, MaxColumns, items.Count);
        var widths = AdaptiveGridMath.CellWidths(finalSize.Width, ColumnSpacing, columns, items.Count, AdaptiveGridMath.ParseWeights(Weights));
        double y = 0;
        for (var row = 0; row * columns < items.Count; row++)
        {
            double rowHeight = 0;
            for (var col = 0; col < columns && row * columns + col < items.Count; col++)
                rowHeight = Math.Max(rowHeight, items[row * columns + col].DesiredSize.Height);
            double x = 0;
            for (var col = 0; col < columns && row * columns + col < items.Count; col++)
            {
                items[row * columns + col].Arrange(new Rect(x, y, widths[col], rowHeight));
                x += widths[col] + ColumnSpacing;
            }
            y += rowHeight + RowSpacing;
        }
        return finalSize;
    }
}
