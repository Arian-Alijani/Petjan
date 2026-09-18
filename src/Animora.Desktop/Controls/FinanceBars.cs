using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Animora.Desktop.Controls;

/// <summary>
/// A rounded horizontal metric track with a proportional fill anchored to the
/// inline-start (right, RTL) edge — the reusable primitive behind the Finance
/// screen's category/product/species progress bars and the donut legend fills
/// (Animora-UI/assets/finance-page.js <c>.fin-list-track</c>/<c>.fin-hbar .track</c>/
/// <c>.fin-leg-fill</c>). Drawn with Avalonia primitives, not a web-style width%.
/// </summary>
public sealed class MetricBar : Control
{
    public static readonly StyledProperty<double> FractionProperty =
        AvaloniaProperty.Register<MetricBar, double>(nameof(Fraction));

    public static readonly StyledProperty<IBrush> FillProperty =
        AvaloniaProperty.Register<MetricBar, IBrush>(nameof(Fill),
            new SolidColorBrush(Color.Parse("#0d9488")));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<MetricBar, IBrush?>(nameof(TrackBrush),
            new SolidColorBrush(Color.Parse("#f1f5f6")));

    /// <summary>Corner radius of the fill/track; a negative value means "half the height" (a pill).</summary>
    public static readonly StyledProperty<double> RadiusProperty =
        AvaloniaProperty.Register<MetricBar, double>(nameof(Radius), -1);

    static MetricBar() => AffectsRender<MetricBar>(FractionProperty, FillProperty, TrackBrushProperty, RadiusProperty);

    /// <summary>Fill fraction, 0..1.</summary>
    public double Fraction
    {
        get => GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public IBrush Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>The track backdrop; set to null/transparent for a bare fill (legend rows).</summary>
    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public double Radius
    {
        get => GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var radius = Radius < 0 ? h / 2 : Radius;

        if (TrackBrush is { } track)
        {
            ctx.DrawRectangle(track, null, new RoundedRect(new Rect(0, 0, w, h), radius));
        }

        var frac = Math.Clamp(Fraction, 0, 1);
        if (frac <= 0)
        {
            return;
        }

        var fw = w * frac;
        // RTL: anchor the fill to the inline-start (right) edge.
        ctx.DrawRectangle(Fill, null, new RoundedRect(new Rect(w - fw, 0, fw, h), radius));
    }
}

/// <summary>One segment of a <see cref="SegmentedBar"/>.</summary>
public sealed record BarSegment(double Fraction, IBrush Fill);

/// <summary>
/// The «خدمات در برابر کالا» split bar (finance.css <c>.svp-bar</c>): a rounded
/// 14px track laid out with a 2px gap, the first segment anchored to the
/// inline-start (right) edge — mirroring the reference's flex row in RTL.
/// </summary>
public sealed class SegmentedBar : Control
{
    public static readonly StyledProperty<IReadOnlyList<BarSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<SegmentedBar, IReadOnlyList<BarSegment>?>(nameof(Segments));

    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<SegmentedBar, double>(nameof(Gap), 2);

    static SegmentedBar() => AffectsRender<SegmentedBar>(SegmentsProperty, GapProperty);

    public IReadOnlyList<BarSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var segments = Segments;
        if (w <= 0 || h <= 0 || segments is null || segments.Count == 0)
        {
            return;
        }

        var radius = h / 2;
        ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#f1f5f6")), null,
            new RoundedRect(new Rect(0, 0, w, h), radius));

        var x = w; // walk right → left (RTL: first segment at the right edge)
        for (var i = 0; i < segments.Count; i++)
        {
            var segW = Math.Max(0, (w * Math.Clamp(segments[i].Fraction, 0, 1)) - (i < segments.Count - 1 ? Gap / 2 : 0));
            x -= segW;
            ctx.DrawRectangle(segments[i].Fill, null, new RoundedRect(new Rect(x, 0, segW, h), radius));
            x -= Gap / 2;
        }
    }
}
