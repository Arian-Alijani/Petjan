using System;
using System.Collections.Generic;

namespace Animora.Desktop.Controls;

/// <summary>
/// Pure angle math shared by <see cref="DonutRing"/> rendering and hit-testing (no Avalonia types).
/// Sweeps are clockwise degrees starting at the top. Only positive values take part: zero/negative/NaN
/// values never contribute to the total and never produce a segment.
/// </summary>
public static class DonutGeometry
{
    /// <summary>Largest fraction of a segment's own sweep that the visual gap may consume.</summary>
    public const double MaxGapFraction = 0.5;

    public readonly record struct Arc(double Start, double End, double SweepStart, double SweepEnd);

    public static double Positive(double value) => double.IsFinite(value) && value > 0 ? value : 0;

    public static double Total(IReadOnlyList<double> values)
    {
        var total = 0.0;
        foreach (var v in values) total += Positive(v);
        return total;
    }

    /// <summary>
    /// Visible arc of one segment. The gap is split between both ends but capped relative to the segment's
    /// own sweep, so any positive share stays visible. Null only for a non-positive value or an empty total.
    /// </summary>
    public static Arc? ArcFor(double value, double total, double precedingSweep, double gapDegrees)
    {
        var v = Positive(value);
        if (v <= 0 || total <= 0) return null;
        var sweep = v / total * 360.0;
        var gap = Math.Min(Math.Max(gapDegrees, 0), sweep * MaxGapFraction);
        var start = -90.0 + precedingSweep;
        return new Arc(start + gap / 2, start + sweep - gap / 2, precedingSweep, precedingSweep + sweep);
    }

    /// <summary>Segment under a clockwise-from-top angle in [0,360), or -1; uses the full (gap-free) sweeps.</summary>
    public static int IndexAt(IReadOnlyList<double> values, double degrees)
    {
        var total = Total(values);
        if (total <= 0) return -1;
        var acc = 0.0;
        for (var i = 0; i < values.Count; i++)
        {
            var sweep = Positive(values[i]) / total * 360.0;
            if (sweep > 0 && degrees >= acc && degrees < acc + sweep) return i;
            acc += sweep;
        }

        return -1;
    }
}
