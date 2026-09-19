using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Animora.Desktop.Controls;

/// <summary>
/// A gapped, hover-focused donut ring drawn directly on the
/// <see cref="DrawingContext"/>. Replaces an ItemsControl-of-Paths whose
/// <c>Canvas</c> items panel measured to zero size (so the coloured segments
/// never rendered).
///
/// The geometry is parameterised so one control backs every ring in the app:
///   • the manager dashboard species donut (viewBox 120, r=48, SW=13, gap 3.4 —
///     styles.css <c>.donut-wrap</c> + dashboard.js <c>bindDonut</c>), and
///   • the Finance channel/payment donut (viewBox 100, r=42, SW=14, gap 3.2 —
///     finance.css <c>.fin-donut-wrap</c> + finance-page.js <c>donut</c>).
///
/// The control is purely visual; pointer hover is translated to a slice index
/// by <see cref="SliceIndexAt"/> (the View hosts it inside a transparent,
/// hit-testable border so the whole disc — including the hollow centre —
/// responds to the pointer).
/// </summary>
public sealed class DonutRing : Control
{
    public static readonly StyledProperty<IReadOnlyList<DonutSlice>?> SlicesProperty =
        AvaloniaProperty.Register<DonutRing, IReadOnlyList<DonutSlice>?>(nameof(Slices));

    public static readonly StyledProperty<IBrush> TrackBrushProperty =
        AvaloniaProperty.Register<DonutRing, IBrush>(nameof(TrackBrush),
            new SolidColorBrush(Color.Parse("#f0f4f2")));

    public static readonly StyledProperty<int> ActiveIndexProperty =
        AvaloniaProperty.Register<DonutRing, int>(nameof(ActiveIndex), -1);

    /// <summary>The SVG viewBox the ring was authored against (square).</summary>
    public static readonly StyledProperty<double> ViewBoxProperty =
        AvaloniaProperty.Register<DonutRing, double>(nameof(ViewBox), 120.0);

    /// <summary>Ring radius in viewBox units.</summary>
    public static readonly StyledProperty<double> RadiusProperty =
        AvaloniaProperty.Register<DonutRing, double>(nameof(Radius), 48.0);

    /// <summary>Ring stroke width in viewBox units.</summary>
    public static readonly StyledProperty<double> StrokeWidthProperty =
        AvaloniaProperty.Register<DonutRing, double>(nameof(StrokeWidth), 13.0);

    /// <summary>Arc-length gap between adjacent slices (viewBox units).</summary>
    public static readonly StyledProperty<double> GapArcProperty =
        AvaloniaProperty.Register<DonutRing, double>(nameof(GapArc), 3.4);

    /// <summary>Extra stroke added to the focused slice (viewBox units).</summary>
    public static readonly StyledProperty<double> ActiveGrowProperty =
        AvaloniaProperty.Register<DonutRing, double>(nameof(ActiveGrow), 4.0);

    /// <summary>Opacity of the non-focused slices while one slice is focused.</summary>
    public static readonly StyledProperty<double> DimOpacityProperty =
        AvaloniaProperty.Register<DonutRing, double>(nameof(DimOpacity), 0.22);

    /// <summary>Round the slice caps (Finance) vs. flat (dashboard species ring).</summary>
    public static readonly StyledProperty<bool> RoundCapsProperty =
        AvaloniaProperty.Register<DonutRing, bool>(nameof(RoundCaps));

    static DonutRing()
    {
        AffectsRender<DonutRing>(
            SlicesProperty, TrackBrushProperty, ActiveIndexProperty,
            ViewBoxProperty, RadiusProperty, StrokeWidthProperty, GapArcProperty,
            ActiveGrowProperty, DimOpacityProperty, RoundCapsProperty);
    }

    /// <summary>The slices to draw (value + HTML hex colour).</summary>
    public IReadOnlyList<DonutSlice>? Slices
    {
        get => GetValue(SlicesProperty);
        set => SetValue(SlicesProperty, value);
    }

    public IBrush TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>Focused slice (-1 = none): grows its stroke and dims the rest.</summary>
    public int ActiveIndex
    {
        get => GetValue(ActiveIndexProperty);
        set => SetValue(ActiveIndexProperty, value);
    }

    public double ViewBox
    {
        get => GetValue(ViewBoxProperty);
        set => SetValue(ViewBoxProperty, value);
    }

    public double Radius
    {
        get => GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    public double StrokeWidth
    {
        get => GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    public double GapArc
    {
        get => GetValue(GapArcProperty);
        set => SetValue(GapArcProperty, value);
    }

    public double ActiveGrow
    {
        get => GetValue(ActiveGrowProperty);
        set => SetValue(ActiveGrowProperty, value);
    }

    public double DimOpacity
    {
        get => GetValue(DimOpacityProperty);
        set => SetValue(DimOpacityProperty, value);
    }

    public bool RoundCaps
    {
        get => GetValue(RoundCapsProperty);
        set => SetValue(RoundCapsProperty, value);
    }

    public override void Render(DrawingContext ctx)
    {
        var view = ViewBox <= 0 ? 120.0 : ViewBox;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 1)
        {
            return;
        }

        var scale = size / view;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = Radius * scale;
        var thickness = StrokeWidth * scale;
        var active = ActiveIndex >= 0;
        var gapDegrees = GapArc / Math.Max(Radius, 1.0) * (180.0 / Math.PI);

        // track — fades slightly while a slice is focused (.dim .donut-track)
        var trackPen = new Pen(TrackBrush, thickness)
        {
            Brush = active ? WithOpacity(TrackBrush, 0.5) : TrackBrush,
        };
        ctx.DrawEllipse(null, trackPen, center, radius, radius);

        var slices = Slices;
        if (slices is null || slices.Count == 0)
        {
            return;
        }

        var total = 0.0;
        foreach (var s in slices)
        {
            total += s.Value;
        }

        if (total <= 0)
        {
            return;
        }

        var cap = RoundCaps ? PenLineCap.Round : PenLineCap.Flat;
        var angle = -90.0;
        for (var i = 0; i < slices.Count; i++)
        {
            var sweep = (slices[i].Value / total) * 360.0;
            var a0 = angle + (gapDegrees / 2);
            var a1 = angle + sweep - (gapDegrees / 2);
            angle += sweep;

            if (a1 <= a0)
            {
                continue;
            }

            var isActive = i == ActiveIndex;
            var brush = ParseBrush(slices[i].Color);
            var opacity = active && !isActive ? DimOpacity : 1.0;
            var stroke = isActive ? (StrokeWidth + ActiveGrow) * scale : thickness;

            DrawArc(ctx, center, radius, a0, a1, WithOpacity(brush, opacity), stroke, cap);
        }
    }

    /// <summary>
    /// Maps a point (in this control's coordinate space) to the slice under it,
    /// or -1 when the pointer is outside the ring's band. Mirrors the arc
    /// layout in <see cref="Render"/>: clockwise from the top.
    /// </summary>
    public int SliceIndexAt(Point p)
    {
        var slices = Slices;
        if (slices is null || slices.Count == 0)
        {
            return -1;
        }

        var view = ViewBox <= 0 ? 120.0 : ViewBox;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 1)
        {
            return -1;
        }

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var dx = p.X - center.X;
        var dy = p.Y - center.Y;
        var dist = Math.Sqrt((dx * dx) + (dy * dy));
        var outer = (size / view * (Radius + (StrokeWidth / 2))) + 4;
        if (dist > outer)
        {
            return -1;
        }

        // Angle in the same clockwise-from-top space the arcs use.
        var deg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        deg = (deg + 90 + 360) % 360;

        var total = 0.0;
        foreach (var s in slices)
        {
            total += s.Value;
        }

        if (total <= 0)
        {
            return -1;
        }

        var acc = 0.0;
        for (var i = 0; i < slices.Count; i++)
        {
            var sweep = slices[i].Value / total * 360.0;
            if (deg >= acc && deg < acc + sweep)
            {
                return i;
            }

            acc += sweep;
        }

        return -1;
    }

    private static void DrawArc(
        DrawingContext ctx, Point center, double radius, double startDeg, double endDeg,
        IBrush brush, double thickness, PenLineCap cap)
    {
        var start = PointOnCircle(center, radius, startDeg);
        var end = PointOnCircle(center, radius, endDeg);
        var sweep = endDeg - startDeg;
        var large = sweep >= 180;

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(start, false);
            g.ArcTo(end, new Size(radius, radius), 0, large, SweepDirection.Clockwise);
            g.EndFigure(false);
        }

        ctx.DrawGeometry(null, new Pen(brush, thickness, lineCap: cap), geo);
    }

    private static Point PointOnCircle(Point c, double r, double deg)
    {
        var rad = deg * Math.PI / 180.0;
        return new Point(c.X + (r * Math.Cos(rad)), c.Y + (r * Math.Sin(rad)));
    }

    private static IBrush WithOpacity(IBrush brush, double opacity)
    {
        if (opacity >= 0.999)
        {
            return brush;
        }

        if (brush is ISolidColorBrush { Color: var c })
        {
            return new SolidColorBrush(Color.FromArgb((byte)Math.Round(c.A * opacity), c.R, c.G, c.B));
        }

        return brush;
    }

    private static IBrush ParseBrush(string hex)
    {
        try
        {
            return new SolidColorBrush(Color.Parse(hex));
        }
        catch (FormatException)
        {
            return Brushes.Gray;
        }
    }
}
