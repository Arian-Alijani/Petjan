using System;
using System.Collections.Generic;
using System.Globalization;
using Animora.Desktop.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Animora.Desktop.Controls;

/// <summary>One point of the Finance trend chart (label = short axis text, full = tooltip text).</summary>
public sealed record TrendChartPoint(double Revenue, double Expense, double Profit, string Label, string Full);

/// <summary>One segment of a Finance donut (value + HTML hex color).</summary>
public sealed record DonutSlice(double Value, string Color);

/// <summary>
/// The Finance main trend chart, re-drawn from the reference's SVG
/// <c>trendChart</c> (Animora-UI/assets/finance-page.js): 5 dashed horizontal
/// gridlines with right-side short-money Y labels, soft gradient area fills for
/// revenue &amp; expense, a crisp smooth profit spline on top and minimal
/// first/mid/last X labels. RTL-aware (the series is plotted newest-at-left).
/// Drawn with Avalonia drawing primitives (CONVENTIONS §4a rule 2), never
/// embedded SVG. Motion (draw-in + hover crosshair) is rendered at its final
/// static value per §4a rule 6.
/// </summary>
public sealed class TrendChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<TrendChartPoint>?> PointsProperty =
        AvaloniaProperty.Register<TrendChart, IReadOnlyList<TrendChartPoint>?>(nameof(Points));

    public static readonly StyledProperty<IBrush> RevenueBrushProperty =
        AvaloniaProperty.Register<TrendChart, IBrush>(nameof(RevenueBrush),
            new SolidColorBrush(Color.Parse("#4a6cf7")));

    public static readonly StyledProperty<IBrush> ExpenseBrushProperty =
        AvaloniaProperty.Register<TrendChart, IBrush>(nameof(ExpenseBrush),
            new SolidColorBrush(Color.Parse("#d92d20")));

    public static readonly StyledProperty<IBrush> ProfitBrushProperty =
        AvaloniaProperty.Register<TrendChart, IBrush>(nameof(ProfitBrush),
            new SolidColorBrush(Color.Parse("#1570cd")));

    static TrendChart() => AffectsRender<TrendChart>(
        PointsProperty, RevenueBrushProperty, ExpenseBrushProperty, ProfitBrushProperty);

    // Hoisted render-time resources (charts re-render on every layout/resize).
    private static readonly IBrush GridBrush = new SolidColorBrush(Color.Parse("#eef2f0"));
    private static readonly IBrush AxisBrush = new SolidColorBrush(Color.Parse("#98a2b3"));
    private static readonly Pen GridPen = new(GridBrush, 1, new DashStyle([2, 6], 0));

    public IReadOnlyList<TrendChartPoint>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public IBrush RevenueBrush
    {
        get => GetValue(RevenueBrushProperty);
        set => SetValue(RevenueBrushProperty, value);
    }

    public IBrush ExpenseBrush
    {
        get => GetValue(ExpenseBrushProperty);
        set => SetValue(ExpenseBrushProperty, value);
    }

    public IBrush ProfitBrush
    {
        get => GetValue(ProfitBrushProperty);
        set => SetValue(ProfitBrushProperty, value);
    }

    // ---- render-pass geometry cache (PERFORMANCE.md §11/§10) ----------------
    // The three area fills, three smooth line splines, the two area gradients and
    // the three line pens are derived purely from the layout (bounds), the Points
    // list and the three series brushes. They are expensive to rebuild
    // (StreamGeometry + Bézier smoothing) yet Render runs on every resize/redraw,
    // so they are rebuilt only when a real input changes and reused otherwise.
    private StreamGeometry? _expArea, _revArea;
    private StreamGeometry? _expLine, _revLine, _proLine;
    private IBrush? _expAreaFill, _revAreaFill;
    private Pen? _expPen, _revPen, _proPen;
    private double _cacheW, _cacheH, _cacheBaseY;
    private IReadOnlyList<TrendChartPoint>? _cacheSource;
    private IBrush? _cacheRevBrush, _cacheExpBrush, _cacheProBrush;

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 1 || h <= 1)
        {
            return;
        }

        // Pad mirrors the reference's SVG viewBox: { t:18, b:26, l:10, r:46 }.
        const double padT = 18, padB = 26, padL = 10, padR = 46;
        var iw = w - padL - padR;
        var ih = h - padT - padB;
        var baseY = padT + ih;

        var gridPen = GridPen;
        var axisBrush = AxisBrush;
        var typeface = Typeface.Default;
        const double axisSize = 9.5;

        var pts = Points;
        if (pts is null || pts.Count == 0 || iw <= 1 || ih <= 1)
        {
            return;
        }

        var n = pts.Count;
        var maxV = 1.0;
        foreach (var p in pts)
        {
            maxV = Math.Max(maxV, Math.Max(p.Revenue, p.Expense));
        }

        maxV *= 1.14;

        double X(int i) => padL + (n == 1 ? iw / 2 : iw * i / (n - 1));
        double Y(double v) => padT + ih - (v / maxV * ih);

        // gridlines + right-side value axis
        for (var i = 0; i <= 4; i++)
        {
            var gy = padT + (ih * i / 4);
            ctx.DrawLine(gridPen, new Point(padL, gy), new Point(w - padR, gy));
            var val = maxV * (1 - (i / 4.0));
            var label = new FormattedText(
                ShortMoney(val), CultureInfo.InvariantCulture, FlowDirection.RightToLeft,
                typeface, axisSize, axisBrush);
            ctx.DrawText(label, new Point(w - padR + 8, gy + 3 - (label.Height / 2)));
        }

        EnsureCache(pts, w, h, baseY, X, Y);

        // soft areas (expense first, revenue on top — as in the reference order)
        if (_expArea is not null && _expAreaFill is not null)
        {
            ctx.DrawGeometry(_expAreaFill, null, _expArea);
        }

        if (_revArea is not null && _revAreaFill is not null)
        {
            ctx.DrawGeometry(_revAreaFill, null, _revArea);
        }

        // lines
        if (_expLine is not null && _expPen is not null)
        {
            ctx.DrawGeometry(null, _expPen, _expLine);
        }

        if (_revLine is not null && _revPen is not null)
        {
            ctx.DrawGeometry(null, _revPen, _revLine);
        }

        if (_proLine is not null && _proPen is not null)
        {
            ctx.DrawGeometry(null, _proPen, _proLine);
        }

        // minimal X labels — newest (index 0), middle and oldest, each centred on its column
        var keep = new HashSet<int> { 0, (n - 1) / 2, n - 1 };
        foreach (var i in keep)
        {
            var text = pts[i].Label;
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            var t = new FormattedText(
                text, CultureInfo.InvariantCulture, FlowDirection.RightToLeft, typeface, 10, axisBrush);
            ctx.DrawText(t, new Point(X(i) - (t.Width / 2), h - padB + 6));
        }
    }

    /// <summary>
    /// (Re)builds the cached area/line geometries, area gradient fills and line
    /// pens — but only when the layout, the point list or a series brush actually
    /// changed. A bare re-render (same inputs) is a no-op. The point/geometry
    /// math is identical to the previous per-frame code; only its timing moved.
    /// </summary>
    private void EnsureCache(
        IReadOnlyList<TrendChartPoint> pts, double w, double h, double baseY,
        Func<int, double> x, Func<double, double> y)
    {
        if (ReferenceEquals(_cacheSource, pts) &&
            _cacheW == w && _cacheH == h && _cacheBaseY == baseY &&
            ReferenceEquals(_cacheRevBrush, RevenueBrush) &&
            ReferenceEquals(_cacheExpBrush, ExpenseBrush) &&
            ReferenceEquals(_cacheProBrush, ProfitBrush))
        {
            return;
        }

        _cacheSource = pts;
        _cacheW = w;
        _cacheH = h;
        _cacheBaseY = baseY;
        _cacheRevBrush = RevenueBrush;
        _cacheExpBrush = ExpenseBrush;
        _cacheProBrush = ProfitBrush;

        var n = pts.Count;
        var revPts = new Point[n];
        var expPts = new Point[n];
        var proPts = new Point[n];
        for (var i = 0; i < n; i++)
        {
            revPts[i] = new Point(x(i), y(pts[i].Revenue));
            expPts[i] = new Point(x(i), y(pts[i].Expense));
            proPts[i] = new Point(x(i), y(Math.Max(0, pts[i].Profit)));
        }

        _expArea = BuildArea(expPts, baseY);
        _revArea = BuildArea(revPts, baseY);
        _expAreaFill = FinanceChartBrushes.Area(ExpenseBrush, 0.16);
        _revAreaFill = FinanceChartBrushes.Area(RevenueBrush, 0.26);

        _expLine = Smooth(expPts);
        _revLine = Smooth(revPts);
        _proLine = Smooth(proPts);
        _expPen = new Pen(ExpenseBrush, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        _revPen = new Pen(RevenueBrush, 2.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        _proPen = new Pen(ProfitBrush, 2.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    }

    private static StreamGeometry BuildArea(Point[] pts, double baseY)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(pts[0].X, baseY), true);
            g.LineTo(pts[0]);
            for (var i = 1; i < pts.Length; i++)
            {
                g.LineTo(pts[i]);
            }

            g.LineTo(new Point(pts[^1].X, baseY));
            g.EndFigure(true);
        }

        return geo;
    }

    /// <summary>Catmull-Rom → cubic Bézier smoothing, mirroring the reference's <c>smoothPath</c>.</summary>
    private static StreamGeometry Smooth(Point[] pts)
    {
        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(pts[0], false);
        for (var i = 0; i < pts.Length - 1; i++)
        {
            var p0 = i > 0 ? pts[i - 1] : pts[i];
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p3 = i + 2 < pts.Length ? pts[i + 2] : p2;
            const double t = 0.16;
            var c1x = p1.X + ((p2.X - p0.X) * t);
            var c1y = p1.Y + ((p2.Y - p0.Y) * t);
            var c2x = p2.X - ((p3.X - p1.X) * t);
            var c2y = p2.Y - ((p3.Y - p1.Y) * t);
            g.CubicBezierTo(new Point(c1x, c1y), new Point(c2x, c2y), p2);
        }

        g.EndFigure(false);
        return geo;
    }

    internal static string ShortMoney(double n)
    {
        if (n >= 1_000_000_000)
        {
            return PersianNumberFormatter.ToPersianDigits((n / 1_000_000_000d).ToString("0.#", CultureInfo.InvariantCulture)) + " میلیارد";
        }

        if (n >= 1_000_000)
        {
            return PersianNumberFormatter.ToPersianDigits((n / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture)) + " م";
        }

        if (n >= 1_000)
        {
            return PersianNumberFormatter.ToPersianDigits(Math.Round(n / 1_000d).ToString(CultureInfo.InvariantCulture)) + " هزار";
        }

        return PersianNumberFormatter.ToPersianDigits(n.ToString("0", CultureInfo.InvariantCulture));
    }
}

/// <summary>Brush builders shared by the Finance chart controls.</summary>
internal static class FinanceChartBrushes
{
    /// <summary>The vertical area gradient under a line (top color@alpha → transparent), matching the SVG linearGradient stops.</summary>
    public static IBrush Area(IBrush line, double topOpacity)
    {
        var c = (line as SolidColorBrush)?.Color ?? Color.Parse("#4a6cf7");
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)Math.Round(255 * topOpacity), c.R, c.G, c.B), 0),
                new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1),
            },
        };
    }
}

/// <summary>
/// A gapped interactive donut ring, re-drawn from the reference's SVG <c>donut</c>
/// (Animora-UI/assets/finance-page.js): a light track circle plus round-capped
/// segments laid out clockwise from the top, matching the reference's
/// <c>r=42</c>/<c>stroke-width=14</c>/<c>gap=3.2</c> geometry. The center value
/// and legend are supplied by the View. Static render (animation final state).
/// </summary>
public sealed class DonutChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<DonutSlice>?> SlicesProperty =
        AvaloniaProperty.Register<DonutChart, IReadOnlyList<DonutSlice>?>(nameof(Slices));

    public static readonly StyledProperty<IBrush> TrackBrushProperty =
        AvaloniaProperty.Register<DonutChart, IBrush>(nameof(TrackBrush),
            new SolidColorBrush(Color.Parse("#f0f4f2")));

    static DonutChart() => AffectsRender<DonutChart>(SlicesProperty, TrackBrushProperty);

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

    // ---- render-pass geometry cache (PERFORMANCE.md §11/§10) ----------------
    // The per-slice arc geometries + pens and the track pen are derived purely
    // from the layout (bounds), the slice list and the track brush. Render runs
    // on every resize/redraw, so they are rebuilt only when a real input changes.
    private StreamGeometry?[] _arcGeometries = Array.Empty<StreamGeometry?>();
    private Pen?[] _arcPens = Array.Empty<Pen?>();
    private Pen? _trackPen;
    private double _cacheSize;
    private double _cacheRadius;
    private Point _cacheCenter;
    private IReadOnlyList<DonutSlice>? _cacheSource;
    private IBrush? _cacheTrackBrush;
    private double _cacheTotal;

    public override void Render(DrawingContext ctx)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 1)
        {
            return;
        }

        var scale = size / 100.0;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = 42 * scale;
        var thickness = 14 * scale;

        var slices = Slices;
        var total = 0.0;
        if (slices is not null)
        {
            foreach (var s in slices)
            {
                total += s.Value;
            }
        }

        EnsureCache(slices, total, center, radius, thickness);

        // track
        if (_trackPen is not null)
        {
            ctx.DrawEllipse(null, _trackPen, center, radius, radius);
        }

        if (slices is null || slices.Count == 0 || total <= 0)
        {
            return;
        }

        for (var i = 0; i < _arcGeometries.Length; i++)
        {
            var geo = _arcGeometries[i];
            var pen = _arcPens[i];
            if (geo is not null && pen is not null)
            {
                ctx.DrawGeometry(null, pen, geo);
            }
        }
    }

    /// <summary>
    /// (Re)builds the cached track pen and per-slice arc geometries/pens — only
    /// when the layout, slice list or track brush actually changed. The arc math
    /// is unchanged; only its timing moved off the per-frame path.
    /// </summary>
    private void EnsureCache(
        IReadOnlyList<DonutSlice>? slices, double total, Point center, double radius, double thickness)
    {
        var count = slices?.Count ?? 0;
        if (ReferenceEquals(_cacheSource, slices) && _cacheSize == thickness &&
            _cacheRadius == radius && _cacheCenter == center && _cacheTotal == total &&
            ReferenceEquals(_cacheTrackBrush, TrackBrush))
        {
            return;
        }

        _cacheSource = slices;
        _cacheSize = thickness;
        _cacheRadius = radius;
        _cacheCenter = center;
        _cacheTotal = total;
        _cacheTrackBrush = TrackBrush;

        if (_trackPen is null || !ReferenceEquals(_trackPen.Brush, TrackBrush) || _trackPen.Thickness != thickness)
        {
            _trackPen = new Pen(TrackBrush, thickness);
        }

        if (_arcGeometries.Length != count)
        {
            _arcGeometries = new StreamGeometry?[count];
            _arcPens = new Pen?[count];
        }

        if (slices is null || count == 0 || total <= 0)
        {
            for (var i = 0; i < _arcGeometries.Length; i++)
            {
                _arcGeometries[i] = null;
                _arcPens[i] = null;
            }

            return;
        }

        // gap in degrees (GAP arc-length 3.2 over r=42)
        var gapDeg = (3.2 / 42.0) * (180.0 / Math.PI);
        var angle = -90.0; // start at the top, clockwise (reference rotate(-90deg))
        for (var i = 0; i < count; i++)
        {
            var sweep = (slices[i].Value / total) * 360.0;
            var a0 = angle + (gapDeg / 2);
            var a1 = angle + sweep - (gapDeg / 2);
            angle += sweep;

            if (a1 <= a0)
            {
                a1 = Math.Min(a0 + 0.01, angle - (gapDeg / 2));
            }

            _arcGeometries[i] = BuildArc(center, radius, a0, a1);
            _arcPens[i] = new Pen(BrushCache.Solid(slices[i].Color), thickness, lineCap: PenLineCap.Round);
        }
    }

    private static StreamGeometry BuildArc(
        Point center, double radius, double startDeg, double endDeg)
    {
        var start = PointOnCircle(center, radius, startDeg);
        var end = PointOnCircle(center, radius, endDeg);
        var sweep = endDeg - startDeg;
        var large = sweep >= 180;

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(start, false);
            // sweep-clockwise in screen space (y-down) → IsLargeArc + SweepDirection.Clockwise
            g.ArcTo(end, new Size(radius, radius), 0, large, SweepDirection.Clockwise);
            g.EndFigure(false);
        }

        return geo;
    }

    private static Point PointOnCircle(Point c, double r, double deg)
    {
        var rad = deg * Math.PI / 180.0;
        return new Point(c.X + (r * Math.Cos(rad)), c.Y + (r * Math.Sin(rad)));
    }
}

/// <summary>
/// The Profit-tab margin half-gauge, re-drawn from the reference's SVG
/// <c>profitGaugeCard</c> (Animora-UI/assets/finance-page.js): a rounded
/// half-ring track plus a value arc from the left end toward the right,
/// proportional to the margin percent. Geometry mirrors the reference's
/// <c>viewBox="0 0 140 82"</c> path <c>M14,74 A52,52 0 0 1 126,74</c> with a
/// 12px rounded stroke. Drawn with Avalonia primitives; the draw-in animation
/// renders at its final value per §4a rule 6.
/// </summary>
public sealed class MarginGauge : Control
{
    public static readonly StyledProperty<double> PercentProperty =
        AvaloniaProperty.Register<MarginGauge, double>(nameof(Percent));

    public static readonly StyledProperty<IBrush> FillProperty =
        AvaloniaProperty.Register<MarginGauge, IBrush>(nameof(Fill),
            new SolidColorBrush(Color.Parse("#1570cd")));

    public static readonly StyledProperty<IBrush> TrackBrushProperty =
        AvaloniaProperty.Register<MarginGauge, IBrush>(nameof(TrackBrush),
            new SolidColorBrush(Color.Parse("#f0f4f2")));

    static MarginGauge() => AffectsRender<MarginGauge>(PercentProperty, FillProperty, TrackBrushProperty);

    /// <summary>The margin percent, 0..100.</summary>
    public double Percent
    {
        get => GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public IBrush Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    // ---- render-pass geometry cache (PERFORMANCE.md §11/§10) ----------------
    // The track arc, value arc and their pens are derived purely from the layout
    // (bounds), the percent and the two brushes. Render runs on every
    // resize/redraw, so they are rebuilt only when a real input changes.
    private StreamGeometry? _trackArc, _valueArc;
    private Pen? _trackPen, _valuePen;
    private double _cacheW, _cacheH, _cachePct;
    private IBrush? _cacheFill, _cacheTrackBrush;

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 1 || h <= 1)
        {
            return;
        }

        EnsureCache(w, h);

        // Track (full half ring, left→right over the top).
        if (_trackArc is not null && _trackPen is not null)
        {
            ctx.DrawGeometry(null, _trackPen, _trackArc);
        }

        // Value arc: from the left end (180°) clockwise over the top, sweep ∝ percent.
        if (_valueArc is not null && _valuePen is not null)
        {
            ctx.DrawGeometry(null, _valuePen, _valueArc);
        }
    }

    /// <summary>
    /// (Re)builds the cached track/value arcs and pens — only when the layout,
    /// percent or a brush actually changed. Geometry math is unchanged.
    /// </summary>
    private void EnsureCache(double w, double h)
    {
        var pct = Math.Clamp(Percent, 0, 100);
        if (_cacheW == w && _cacheH == h && _cachePct == pct &&
            ReferenceEquals(_cacheFill, Fill) && ReferenceEquals(_cacheTrackBrush, TrackBrush))
        {
            return;
        }

        _cacheW = w;
        _cacheH = h;
        _cachePct = pct;
        _cacheFill = Fill;
        _cacheTrackBrush = TrackBrush;

        // Reference viewBox is 140×82 with r=52, arc ends at (14,74) and (126,74).
        var scale = Math.Min(w / 140.0, h / 82.0);
        var r = 52 * scale;
        var cx = w / 2;
        var cy = 74 * scale;
        var thickness = 12 * scale;

        _trackArc = BuildArc(cx, cy, r, 180, 0);
        _trackPen = new Pen(TrackBrush, thickness, lineCap: PenLineCap.Round);

        if (pct <= 0)
        {
            _valueArc = null;
            _valuePen = null;
            return;
        }

        _valueArc = BuildArc(cx, cy, r, 180, 180 + (pct * 1.8));
        _valuePen = new Pen(Fill, thickness, lineCap: PenLineCap.Round);
    }

    private static StreamGeometry BuildArc(
        double cx, double cy, double r, double startDeg, double endDeg)
    {
        var start = new Point(cx + (r * Math.Cos(startDeg * Math.PI / 180.0)), cy + (r * Math.Sin(startDeg * Math.PI / 180.0)));
        var end = new Point(cx + (r * Math.Cos(endDeg * Math.PI / 180.0)), cy + (r * Math.Sin(endDeg * Math.PI / 180.0)));
        var sweep = Math.Abs(endDeg - startDeg);
        var large = sweep >= 180;

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(start, false);
            // The reference path (M14,74 A52,52 0 0 1 126,74) sweeps clockwise
            // over the top (SVG sweep-flag 1), which maps to Avalonia's Clockwise
            // in this y-down space — CounterClockwise would draw the bottom half.
            g.ArcTo(end, new Size(r, r), 0, large, SweepDirection.Clockwise);
            g.EndFigure(false);
        }

        return geo;
    }
}
