using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Animora.Desktop.Controls;

/// <summary>Pure column/width math of <see cref="AdaptiveGrid"/> (no Avalonia types, unit-testable).</summary>
public static class AdaptiveGridMath
{
    /// <summary>
    /// Columns that fit in <paramref name="available"/> while every cell stays at least <paramref name="minItemWidth"/>
    /// wide; clamped to [1, min(maxColumns, itemCount)]. Infinite/unknown width means "as wide as allowed".
    /// </summary>
    public static int Columns(double available, double spacing, double minItemWidth, int maxColumns, int itemCount)
    {
        if (itemCount <= 0) return 1;
        var cap = Math.Max(1, Math.Min(maxColumns <= 0 ? itemCount : maxColumns, itemCount));
        if (!double.IsFinite(available)) return cap;
        spacing = Math.Max(0, spacing);
        minItemWidth = Math.Max(1, minItemWidth);
        var fit = (int)Math.Floor((Math.Max(0, available) + spacing) / (minItemWidth + spacing));
        return Math.Clamp(fit, 1, cap);
    }

    /// <summary>
    /// Cell widths for one row of <paramref name="columns"/> cells. Weights apply only when every item sits on a
    /// single row (so a stacked layout is never skewed); otherwise cells are equal.
    /// </summary>
    public static double[] CellWidths(double available, double spacing, int columns, int itemCount, IReadOnlyList<double>? weights)
    {
        columns = Math.Max(1, columns);
        var usable = Math.Max(0, available - Math.Max(0, spacing) * (columns - 1));
        var widths = new double[columns];
        var useWeights = weights is { Count: > 0 } && columns == itemCount && weights.Count >= columns
                         && weights.Take(columns).All(w => double.IsFinite(w) && w > 0);
        var sum = useWeights ? weights!.Take(columns).Sum() : columns;
        for (var i = 0; i < columns; i++) widths[i] = usable * (useWeights ? weights![i] / sum : 1.0 / columns);
        return widths;
    }

    public static IReadOnlyList<double>? ParseWeights(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<double>(parts.Length);
        foreach (var p in parts)
        {
            if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) || !double.IsFinite(w) || w <= 0) return null;
            list.Add(w);
        }
        return list;
    }
}
