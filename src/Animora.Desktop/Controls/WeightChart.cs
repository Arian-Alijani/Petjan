using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using Animora.Desktop.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Animora.Desktop.Controls;

/// <summary>One point on a <see cref="WeightChart"/> (label = full Jalali date, short = axis label).</summary>
public sealed record WeightChartPoint(string Label, string ShortLabel, double Value);

/// <summary>
/// Line + area growth chart for the pet file's weight panels. Same visual language
/// as the dashboard's <see cref="RevenuePulseChart"/> — a single smooth
/// (Catmull-Rom → cubic Bézier) line, a soft vertical gradient area, hollow round
/// markers and faint dashed gridlines — but painted in ONE colour (the brand blue,
/// overridable through <see cref="LineBrush"/>) instead of the dashboard's
/// multi-hue gradient.
/// <para>
/// Axis labels are numeric / Jalali dates, so each one is wrapped in a
/// LEFT-TO-RIGHT ISOLATE (U+2066 … U+2069) and rendered with an LTR paragraph
/// base. That is the W3C-standard bidi fix for strings that otherwise get
/// reordered by the surrounding RTL context (which is what reversed the weight
/// and date labels).
/// </para>
/// <para>
/// The control observes <see cref="INotifyCollectionChanged"/> on the bound
/// <see cref="Points"/> collection, so a weight added while this chart is on
/// screen repaints immediately — no need to leave and re-open the pet file. The
/// geometry cache is dropped on every collection change (PERFORMANCE.md §10/§11:
/// the expensive work still only runs when something really changed).
/// </para>
/// </summary>
public sealed class WeightChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<WeightChartPoint>?> PointsProperty =
        AvaloniaProperty.Register<WeightChart, IReadOnlyList<WeightChartPoint>?>(nameof(Points));

    public static readonly StyledProperty<IBrush> LineBrushProperty =
        AvaloniaProperty.Register<WeightChart, IBrush>(nameof(LineBrush),
            new SolidColorBrush(Color.Parse("#4a6cf7")));

    /// <summary>Fallback tint when <see cref="LineBrush"/> is not a solid colour.</summary>
    private static readonly Color FallbackAccent = Color.Parse("#4a6cf7");

    // Hoisted render-time resources: a chart re-renders on every layout/resize,
    // so allocating pens/brushes per Render() pass is avoidable allocation in a
    // hot path (PERFORMANCE.md §10). These are immutable and shared.
    private static readonly IBrush GridBrush = new SolidColorBrush(Color.Parse("#f0f1f6"));
    private static readonly IBrush AxisBrush = new SolidColorBrush(Color.Parse("#9e9eb3"));
    private static readonly IBrush EmptyTextBrush = new SolidColorBrush(Color.Parse("#98a2b3"));
    private static readonly IBrush DotFill = new SolidColorBrush(Colors.White);
    private static readonly IBrush TooltipFill = new SolidColorBrush(Color.Parse("#080a21"));
    private static readonly IBrush TooltipCaption = new SolidColorBrush(Color.Parse("#8e95aa"));
    private static readonly IBrush TooltipValue = new SolidColorBrush(Colors.White);
    private static readonly Pen GridPen = new(GridBrush, 1);
    private static readonly Pen GridDashPen = new(GridBrush, 1, new DashStyle([3, 4], 0));
    private static readonly Pen GuidePen = new(new SolidColorBrush(Color.Parse("#8e95aa")), 1.4, new DashStyle([2, 4], 0));
    private static readonly Typeface ChartTypeface = new(FontFamily.Default);
    private static readonly Typeface ChartTypefaceBold = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

    // Layout padding (device-independent px). Left gutter holds the value axis,
    // bottom holds the date labels, top gives the line breathing room.
    private const double PadT = 18, PadR = 12, PadB = 26, PadL = 42;
    private const int Ticks = 4;
    private const double DotRadius = 3.6;
    private const double DotRadiusActive = 5.2;

    // Unicode bidi isolates (see class summary): pin numeric/date text to LTR.
    private const char Lri = '\u2066';
    private const char Pdi = '\u2069';
    private static string Ltr(string s) => string.Concat(Lri, s, Pdi);

    // Hover state: read only by this control, wired to AffectsRender through a
    // private styled property so a pointer move never allocates or rebinds.
    private static readonly StyledProperty<int> HoverIndexProperty =
        AvaloniaProperty.Register<WeightChart, int>("HoverIndex", -1);

    static WeightChart()
    {
        AffectsRender<WeightChart>(PointsProperty, LineBrushProperty, HoverIndexProperty);
    }

    // ---- render-pass cache (PERFORMANCE.md §11/§10) -------------------------
    private StreamGeometry? _areaGeometry, _lineGeometry;
    private IBrush? _areaBrush;
    private Pen? _linePen, _dotPen, _shadowPen;
    private double[]? _px, _py, _tickValues;
    private double _cacheW, _cacheH;
    private IReadOnlyList<WeightChartPoint>? _cacheSource;
    private IBrush? _cacheLineBrush;

    // The bound collection is mutated in place (Clear/Add), so its reference
    // never changes; we track it to drop the cache the moment its contents move.
    private INotifyCollectionChanged? _observed;

    /// <summary>Chart points (already Jalali-formatted). Null/empty → the "no data" text.</summary>
    public IReadOnlyList<WeightChartPoint>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    /// <summary>Line/marker colour (single accent colour by default).</summary>
    public IBrush LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    private int HoverIndex
    {
        get => GetValue(HoverIndexProperty);
        set => SetValue(HoverIndexProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PointsProperty)
        {
            if (_observed is not null)
            {
                _observed.CollectionChanged -= OnPointsCollectionChanged;
            }

            _observed = change.NewValue as INotifyCollectionChanged;
            if (_observed is not null)
            {
                _observed.CollectionChanged += OnPointsCollectionChanged;
            }

            ResetCache();
        }
        else if (change.Property == LineBrushProperty)
        {
            ResetCache();
        }
    }

    private void OnPointsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ResetCache();

    private void ResetCache()
    {
        _cacheSource = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var points = Points;
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 1 || h <= 1)
        {
            return;
        }

        // Transparent fill keeps the whole surface hit-testable (hover anywhere).
        ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));

        if (points is null || points.Count == 0)
        {
            var empty = new FormattedText(
                "داده‌ای ثبت نشده است", CultureInfo.InvariantCulture, FlowDirection.RightToLeft,
                ChartTypeface, 12, EmptyTextBrush);
            ctx.DrawText(empty, new Point((w - empty.Width) / 2, h / 2 - empty.Height / 2));
            return;
        }

        EnsureCache(points, w, h);
        var px = _px!;
        var py = _py!;

        var bottom = h - PadB;

        // ── gridlines + left value axis (dashed except the baseline) ──
        for (var i = 0; i <= Ticks; i++)
        {
            var y = PadT + ((bottom - PadT) * i / Ticks);
            ctx.DrawLine(i == Ticks ? GridPen : GridDashPen, new Point(PadL, y), new Point(w - PadR, y));

            var label = new FormattedText(
                Ltr(PersianLabel(_tickValues![i])), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, ChartTypeface, 9.5, AxisBrush);
            ctx.DrawText(label, new Point(PadL - 7 - label.Width, y - (label.Height / 2)));
        }

        // ── soft gradient area, dense shadow, then the smooth line ──
        if (_areaGeometry is not null && _areaBrush is not null)
        {
            ctx.DrawGeometry(_areaBrush, null, _areaGeometry);
        }

        if (_lineGeometry is not null && _shadowPen is not null)
        {
            using (ctx.PushTransform(Matrix.CreateTranslation(0, 4)))
            {
                ctx.DrawGeometry(null, _shadowPen, _lineGeometry);
            }
        }

        if (_lineGeometry is not null && _linePen is not null)
        {
            ctx.DrawGeometry(null, _linePen, _lineGeometry);
        }

        var hover = HoverIndex;
        var hasHover = hover >= 0 && hover < px.Length;

        if (hasHover)
        {
            ctx.DrawLine(GuidePen, new Point(px[hover], py[hover] + DotRadiusActive + 1), new Point(px[hover], bottom));
        }

        // ── hollow round markers ──
        var dotPen = _dotPen;
        if (dotPen is not null)
        {
            for (var i = 0; i < px.Length; i++)
            {
                var r = hasHover && i == hover ? DotRadiusActive : DotRadius;
                ctx.DrawEllipse(DotFill, dotPen, new Point(px[i], py[i]), r, r);
            }
        }

        // ── x-axis date labels (thinned when crowded) ──
        var step = px.Length > 6 ? (int)System.Math.Ceiling(px.Length / 6.0) : 1;
        for (var i = 0; i < px.Length; i++)
        {
            if (px.Length > 6 && i % step != 0 && i != px.Length - 1)
            {
                continue;
            }

            var text = points[i].ShortLabel;
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            var t = new FormattedText(
                Ltr(text), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                ChartTypeface, 9.5, AxisBrush);
            var cx = System.Math.Clamp(px[i] - (t.Width / 2), PadL, w - PadR - t.Width);
            ctx.DrawText(t, new Point(cx, bottom + 6));
        }

        if (hasHover)
        {
            DrawTooltip(ctx, points[hover], px[hover], py[hover], w);
        }
    }

    private void DrawTooltip(DrawingContext ctx, WeightChartPoint point, double anchorX, double anchorY, double w)
    {
        // Date caption: numeric Jalali date → LTR isolate (RTL would reverse it).
        var caption = new FormattedText(
            Ltr(point.Label), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            ChartTypeface, 10, TooltipCaption);

        var valueText = PersianNumberFormatter.ToPersianDigits(Number(point.Value)) + " کیلوگرم";
        var value = new FormattedText(
            Ltr(valueText), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            ChartTypefaceBold, 13, TooltipValue);

        const double padX = 11, padY = 7, gap = 2, pointer = 4.5, markerGap = 9;
        var boxW = System.Math.Max(caption.Width, value.Width) + (padX * 2);
        var boxH = caption.Height + gap + value.Height + (padY * 2);

        var boxX = System.Math.Clamp(anchorX - (boxW / 2), PadL, System.Math.Max(PadL, w - PadR - boxW));
        var boxY = anchorY - markerGap - pointer - boxH;
        if (boxY < 2)
        {
            boxY = anchorY + markerGap + pointer;
        }

        ctx.DrawRectangle(TooltipFill, null, new RoundedRect(new Rect(boxX, boxY, boxW, boxH), 9));

        var tipX = System.Math.Clamp(anchorX, boxX + 10, boxX + boxW - 10);
        var pointsAbove = boxY + boxH <= anchorY;
        var tri = new StreamGeometry();
        using (var g = tri.Open())
        {
            var baseY = pointsAbove ? boxY + boxH : boxY;
            var dir = pointsAbove ? pointer : -pointer;
            g.BeginFigure(new Point(tipX - pointer, baseY), true);
            g.LineTo(new Point(tipX + pointer, baseY));
            g.LineTo(new Point(tipX, baseY + dir));
            g.EndFigure(true);
        }

        ctx.DrawGeometry(TooltipFill, null, tri);
        ctx.DrawText(caption, new Point(boxX + ((boxW - caption.Width) / 2), boxY + padY));
        ctx.DrawText(value, new Point(boxX + ((boxW - value.Width) / 2), boxY + padY + caption.Height + gap));
    }

    /// <summary>
    /// (Re)builds point coordinates, tick values, the smooth area/line geometries
    /// and the pens — only when the layout, the data or the brush actually
    /// changed. Called from Render, so a bare hover re-render is a no-op here.
    /// </summary>
    private void EnsureCache(IReadOnlyList<WeightChartPoint> points, double w, double h)
    {
        if (ReferenceEquals(_cacheSource, points) && _cacheW == w && _cacheH == h &&
            ReferenceEquals(_cacheLineBrush, LineBrush) && _px is not null)
        {
            return;
        }

        _cacheSource = points;
        _cacheW = w;
        _cacheH = h;
        _cacheLineBrush = LineBrush;

        var iw = w - PadL - PadR;
        var ih = h - PadT - PadB;
        var bottom = PadT + ih;

        var min = double.MaxValue;
        var max = double.MinValue;
        for (var i = 0; i < points.Count; i++)
        {
            min = System.Math.Min(min, points[i].Value);
            max = System.Math.Max(max, points[i].Value);
        }

        var span = (max - min) > 0 ? (max - min) : 1;
        min = System.Math.Max(0, min - (span * 0.22));
        max += span * 0.22;
        var range = max - min;

        var n = points.Count;
        var px = new double[n];
        var py = new double[n];
        var geoPts = new Point[n];
        for (var i = 0; i < n; i++)
        {
            px[i] = PadL + (n <= 1 ? iw / 2 : (iw * i) / (n - 1));
            py[i] = bottom - (((points[i].Value - min) / range) * ih);
            geoPts[i] = new Point(px[i], py[i]);
        }

        var tickValues = new double[Ticks + 1];
        for (var i = 0; i <= Ticks; i++)
        {
            tickValues[i] = min + (range * i / Ticks);
        }

        _px = px;
        _py = py;
        _tickValues = tickValues;

        // Single point: draw a flat baseline segment so the marker + area read correctly.
        _lineGeometry = n <= 1
            ? FlatSegment(geoPts[0], bottom)
            : Smooth(geoPts, closeToBaseline: false, bottom);
        _areaGeometry = n <= 1
            ? FlatArea(geoPts[0], bottom)
            : Smooth(geoPts, closeToBaseline: true, bottom);

        var accent = LineBrush is ISolidColorBrush scb ? scb.Color : FallbackAccent;

        _areaBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x2E, accent.R, accent.G, accent.B), 0.0),
                new GradientStop(Color.FromArgb(0x14, accent.R, accent.G, accent.B), 0.55),
                new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1.0),
            },
        };

        _linePen = new Pen(LineBrush, 3.2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        _dotPen = new Pen(LineBrush, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        _shadowPen = new Pen(
            new SolidColorBrush(Color.FromArgb(0x2A, accent.R, accent.G, accent.B)),
            5.2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    }

    private static StreamGeometry FlatSegment(Point p, double baseline)
    {
        var geo = new StreamGeometry();
        using var g = geo.Open();
        var y = p.Y > 0 ? p.Y : baseline - 1;
        g.BeginFigure(new Point(p.X - 6, y), false);
        g.LineTo(new Point(p.X + 6, y));
        g.EndFigure(false);
        return geo;
    }

    private static StreamGeometry FlatArea(Point p, double baseline)
    {
        var geo = new StreamGeometry();
        using var g = geo.Open();
        var y = p.Y > 0 ? p.Y : baseline - 1;
        g.BeginFigure(new Point(p.X - 6, baseline), true);
        g.LineTo(new Point(p.X - 6, y));
        g.LineTo(new Point(p.X + 6, y));
        g.LineTo(new Point(p.X + 6, baseline));
        g.EndFigure(true);
        return geo;
    }

    /// <summary>
    /// Catmull-Rom → cubic Bézier smoothing (mirrors the dashboard chart's
    /// smoothing so curves read consistently across the app). When
    /// <paramref name="closeToBaseline"/> is set the figure is closed down to the
    /// baseline to form the fillable area.
    /// </summary>
    private static StreamGeometry Smooth(Point[] pts, bool closeToBaseline, double baseline)
    {
        var geo = new StreamGeometry();
        using var g = geo.Open();
        if (closeToBaseline)
        {
            g.BeginFigure(new Point(pts[0].X, baseline), true);
            g.LineTo(pts[0]);
        }
        else
        {
            g.BeginFigure(pts[0], false);
        }

        for (var i = 0; i < pts.Length - 1; i++)
        {
            var p0 = i > 0 ? pts[i - 1] : pts[i];
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p3 = i + 2 < pts.Length ? pts[i + 2] : p2;
            const double t = 0.16;
            g.CubicBezierTo(
                new Point(p1.X + ((p2.X - p0.X) * t), p1.Y + ((p2.Y - p0.Y) * t)),
                new Point(p2.X - ((p3.X - p1.X) * t), p2.Y - ((p3.Y - p1.Y) * t)),
                p2);
        }

        if (closeToBaseline)
        {
            g.LineTo(new Point(pts[^1].X, baseline));
            g.EndFigure(true);
        }
        else
        {
            g.EndFigure(false);
        }

        return geo;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var px = _px;
        if (px is null || px.Length == 0)
        {
            return;
        }

        var x = e.GetPosition(this).X;
        var nearest = 0;
        var best = double.MaxValue;
        for (var i = 0; i < px.Length; i++)
        {
            var d = System.Math.Abs(px[i] - x);
            if (d < best)
            {
                best = d;
                nearest = i;
            }
        }

        if (HoverIndex != nearest)
        {
            HoverIndex = nearest;
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (HoverIndex != -1)
        {
            HoverIndex = -1;
        }
    }

    private static string Number(double v) =>
        v < 10 ? v.ToString("0.##", CultureInfo.InvariantCulture)
               : v.ToString("0.#", CultureInfo.InvariantCulture);

    private static string PersianLabel(double v) =>
        PersianNumberFormatter.ToPersianDigits(Number(v));
}
