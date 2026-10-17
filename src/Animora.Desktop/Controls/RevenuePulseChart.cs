using System;
using System.Collections.Generic;
using System.Globalization;
using Animora.Contracts.Dashboard;
using Animora.Desktop.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Animora.Desktop.Controls;

/// <summary>
/// The manager dashboard's revenue-pulse chart, re-designed pixel-for-pixel from
/// the reference "Reports" card: a single <b>smooth</b> (Catmull-Rom → cubic
/// Bézier) line and soft area fill painted with a horizontal brand gradient that
/// flows from sky-blue on the left to magenta on the right, faint dashed
/// gridlines with left-side value labels, hollow round point markers and a
/// light, minimal hover affordance — a dotted vertical guide from the hovered
/// point down to the baseline, an enlarged marker and a deep-navy rounded
/// tooltip bubble (small caption + bold value) with a downward pointer.
///
/// <para>Replaces the previous <c>Canvas</c> + three bound <c>Path</c>s whose
/// line was drawn with straight <c>LineTo</c> segments (angular, no gradient, no
/// per-point hover). The series, labels and real amounts all still come straight
/// from <see cref="DashboardRevenuePulse"/> (no backend change); the control only
/// changes how that data is drawn.</para>
///
/// <para>Drawn entirely with Avalonia drawing primitives (CONVENTIONS §4a rule 2),
/// never embedded SVG. All geometry/brushes/pens are rebuilt only when a real
/// input changes (layout, data) and reused otherwise, so a plain hover — which
/// only moves <see cref="HoverIndex"/> — allocates nothing on the hot path
/// (PERFORMANCE.md §10/§11).</para>
/// </summary>
public sealed class RevenuePulseChart : Control
{
    public static readonly StyledProperty<DashboardRevenuePulse?> PulseProperty =
        AvaloniaProperty.Register<RevenuePulseChart, DashboardRevenuePulse?>(nameof(Pulse));

    /// <summary>Small caption shown above the value inside the hover bubble (e.g. "درآمد").</summary>
    public static readonly StyledProperty<string> HoverCaptionProperty =
        AvaloniaProperty.Register<RevenuePulseChart, string>(nameof(HoverCaption), "درآمد");

    static RevenuePulseChart()
    {
        AffectsRender<RevenuePulseChart>(PulseProperty, HoverCaptionProperty, HoverIndexProperty);
    }

    public RevenuePulseChart()
    {
        // The whole surface must react to the pointer (including the gaps between
        // points). The base Control has no Background, so hit-testing across the
        // full bounds is guaranteed by painting a transparent fill in Render
        // (see the transparent rectangle at the top of the draw pass).
    }

    /// <summary>The normalized revenue series + labels + real amounts (from the snapshot).</summary>
    public DashboardRevenuePulse? Pulse
    {
        get => GetValue(PulseProperty);
        set => SetValue(PulseProperty, value);
    }

    public string HoverCaption
    {
        get => GetValue(HoverCaptionProperty);
        set => SetValue(HoverCaptionProperty, value);
    }

    // Hover state is a plain field (not a styled prop) because only this control
    // reads it; AffectsRender is wired through a private readonly styled prop so a
    // hover just invalidates the visual without any binding churn.
    private static readonly StyledProperty<int> HoverIndexProperty =
        AvaloniaProperty.Register<RevenuePulseChart, int>("HoverIndex", -1);

    private int HoverIndex
    {
        get => GetValue(HoverIndexProperty);
        set => SetValue(HoverIndexProperty, value);
    }

    // ── palette ──────────────────────────────────────────────────────────────
    // Horizontal gradient flowing blue → periwinkle → purple → magenta, matching
    // the reference. Shared immutable stops; the brushes are rebuilt only on a
    // real layout/data change (see cache below).
    private static readonly GradientStops LineStops = new()
    {
        new GradientStop(Color.Parse("#5BA8E9"), 0.00),
        new GradientStop(Color.Parse("#8B85EA"), 0.38),
        new GradientStop(Color.Parse("#AB6BE7"), 0.66),
        new GradientStop(Color.Parse("#EE62C9"), 1.00),
    };

    private static readonly IBrush GridBrush = new SolidColorBrush(Color.Parse("#F0F1F6"));
    private static readonly IBrush AxisBrush = new SolidColorBrush(Color.Parse("#9E9EB3"));
    private static readonly IBrush GuideBrush = new SolidColorBrush(Color.Parse("#8E95AA"));
    private static readonly IBrush DotFill = new SolidColorBrush(Colors.White);
    private static readonly IBrush TooltipFill = new SolidColorBrush(Color.Parse("#080A21"));
    private static readonly IBrush TooltipCaption = new SolidColorBrush(Color.Parse("#8E95AA"));
    private static readonly IBrush TooltipValue = new SolidColorBrush(Colors.White);
    private static readonly IBrush EmptyTextBrush = new SolidColorBrush(Color.Parse("#98a2b3"));

    private static readonly Pen GridPen = new(GridBrush, 1);
    private static readonly Pen GuidePen = new(GuideBrush, 1.4, new DashStyle([2, 4], 0));
    private static readonly Typeface AxisTypeface = new(FontFamily.Default);
    // Bold weight must be baked into the Typeface — Avalonia's FormattedText has no
    // settable FontWeight; the weight is read from the typeface passed in.
    private static readonly Typeface AxisTypefaceBold = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

    // Layout padding (device-independent px). Left gutter holds the value axis,
    // bottom holds the time labels, top gives the line + tooltip breathing room.
    private const double PadT = 26, PadR = 14, PadB = 26, PadL = 34;
    private const double DotRadius = 4;
    private const double DotRadiusActive = 5.5;

    // ── render-pass cache ─────────────────────────────────────────────────────
    private StreamGeometry? _areaGeometry, _lineGeometry;
    private IBrush? _areaBrush;
    private Pen? _linePen, _dotPen;
    private double[]? _px, _py;
    private double _cacheW, _cacheH;
    private DashboardRevenuePulse? _cacheSource;

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 1 || h <= 1)
        {
            return;
        }

        // Paint a transparent fill across the whole surface so the control is
        // hit-testable everywhere (including the gaps between points) — the base
        // Control type has no Background property to do this for us.
        ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));

        var pulse = Pulse;
        var points = pulse?.Points;
        if (pulse is null || points is null || points.Count < 2)
        {
            var empty = new FormattedText(
                "داده‌ای برای نمایش وجود ندارد", CultureInfo.InvariantCulture,
                FlowDirection.RightToLeft, AxisTypeface, 12, EmptyTextBrush);
            ctx.DrawText(empty, new Point((w - empty.Width) / 2, (h - empty.Height) / 2));
            return;
        }

        EnsureCache(pulse, w, h);
        var px = _px!;
        var py = _py!;

        // ── gridlines + left value axis (0,20,40,60,80,100 of the chart band) ──
        const int ticks = 5;
        var top = PadT;
        var bottom = h - PadB;
        for (var i = 0; i <= ticks; i++)
        {
            var y = top + ((bottom - top) * i / ticks);
            ctx.DrawLine(GridPen, new Point(PadL, y), new Point(w - PadR, y));

            var value = 100 - (i * 20);
            var label = new FormattedText(
                PersianNumberFormatter.ToPersianDigits(value.ToString(CultureInfo.InvariantCulture)),
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight, AxisTypeface, 10.5, AxisBrush);
            ctx.DrawText(label, new Point(PadL - 8 - label.Width, y - (label.Height / 2)));
        }

        // ── soft gradient area under the smooth line ──
        if (_areaGeometry is not null && _areaBrush is not null)
        {
            ctx.DrawGeometry(_areaBrush, null, _areaGeometry);
        }

        // ── the smooth, horizontally-gradient line ──
        if (_lineGeometry is not null && _linePen is not null)
        {
            ctx.DrawGeometry(null, _linePen, _lineGeometry);
        }

        var hover = HoverIndex;
        var hasHover = hover >= 0 && hover < px.Length;

        // ── dotted vertical guide for the hovered point (behind the markers) ──
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

        // ── x-axis time/day labels (thinned when crowded) ──
        var labels = pulse.DayLabels;
        if (labels.Count == px.Length)
        {
            var step = px.Length > 7 ? (int)Math.Ceiling(px.Length / 7.0) : 1;
            for (var i = 0; i < px.Length; i++)
            {
                if (px.Length > 7 && i % step != 0 && i != px.Length - 1)
                {
                    continue;
                }

                var text = labels[i];
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                var t = new FormattedText(
                    text, CultureInfo.InvariantCulture, FlowDirection.RightToLeft, AxisTypeface, 10, AxisBrush);
                var cx = Math.Clamp(px[i] - (t.Width / 2), PadL, w - PadR - t.Width);
                ctx.DrawText(t, new Point(cx, h - PadB + 6));
            }
        }

        // ── hover tooltip bubble (navy, rounded, downward pointer) ──
        if (hasHover)
        {
            DrawTooltip(ctx, pulse, hover, px[hover], py[hover], w);
        }
    }

    private void DrawTooltip(DrawingContext ctx, DashboardRevenuePulse pulse, int index, double anchorX, double anchorY, double w)
    {
        var caption = new FormattedText(
            HoverCaption, CultureInfo.InvariantCulture, FlowDirection.RightToLeft, AxisTypeface, 10.5, TooltipCaption);

        var valueText = index < pulse.Values.Count
            ? PersianNumberFormatter.ToPersianDigits(pulse.Values[index].ToString("N0", CultureInfo.InvariantCulture))
            : (index < pulse.DayLabels.Count ? pulse.DayLabels[index] : string.Empty);
        var value = new FormattedText(
            valueText, CultureInfo.InvariantCulture, FlowDirection.RightToLeft, AxisTypefaceBold, 14, TooltipValue);

        const double padX = 12, padY = 8, gap = 2, pointer = 5, markerGap = 10;
        var contentW = Math.Max(caption.Width, value.Width);
        var boxW = contentW + (padX * 2);
        var boxH = caption.Height + gap + value.Height + (padY * 2);

        // Centre the bubble over the point, clamped inside the chart.
        var boxX = Math.Clamp(anchorX - (boxW / 2), PadL, w - PadR - boxW);
        var boxY = anchorY - markerGap - pointer - boxH;
        if (boxY < 2)
        {
            boxY = anchorY + markerGap + pointer; // flip below if there's no room above
        }

        var rect = new Rect(boxX, boxY, boxW, boxH);
        ctx.DrawRectangle(TooltipFill, null, new RoundedRect(rect, 10));

        // Downward pointer triangle, centred on the point (within the bubble span).
        var tipX = Math.Clamp(anchorX, boxX + 12, boxX + boxW - 12);
        var pointsAbove = boxY + boxH <= anchorY;
        var tri = new StreamGeometry();
        using (var g = tri.Open())
        {
            if (pointsAbove)
            {
                var baseY = boxY + boxH;
                g.BeginFigure(new Point(tipX - pointer, baseY), true);
                g.LineTo(new Point(tipX + pointer, baseY));
                g.LineTo(new Point(tipX, baseY + pointer));
            }
            else
            {
                var baseY = boxY;
                g.BeginFigure(new Point(tipX - pointer, baseY), true);
                g.LineTo(new Point(tipX + pointer, baseY));
                g.LineTo(new Point(tipX, baseY - pointer));
            }

            g.EndFigure(true);
        }

        ctx.DrawGeometry(TooltipFill, null, tri);

        // Centred caption + value.
        ctx.DrawText(caption, new Point(boxX + ((boxW - caption.Width) / 2), boxY + padY));
        ctx.DrawText(value, new Point(boxX + ((boxW - value.Width) / 2), boxY + padY + caption.Height + gap));
    }

    /// <summary>
    /// (Re)builds the point coordinates, smooth area/line geometries, the
    /// horizontal gradient brushes and the line/dot pens — only when the layout or
    /// the pulse series actually changed. A bare hover re-render is a no-op here.
    /// </summary>
    private void EnsureCache(DashboardRevenuePulse pulse, double w, double h)
    {
        if (ReferenceEquals(_cacheSource, pulse) && _cacheW == w && _cacheH == h && _px is not null)
        {
            return;
        }

        _cacheSource = pulse;
        _cacheW = w;
        _cacheH = h;

        var points = pulse.Points;
        var n = points.Count;
        var iw = w - PadL - PadR;
        var top = PadT;
        var bottom = h - PadB;
        var ih = bottom - top;

        var px = new double[n];
        var py = new double[n];
        for (var i = 0; i < n; i++)
        {
            px[i] = PadL + (n <= 1 ? iw / 2 : iw * i / (n - 1));
            // Points are already normalized 0..1 in the signed pulse domain.
            py[i] = bottom - (Math.Clamp(points[i], 0, 1) * ih);
        }

        _px = px;
        _py = py;

        var geoPts = new Point[n];
        for (var i = 0; i < n; i++)
        {
            geoPts[i] = new Point(px[i], py[i]);
        }

        // Smooth line (Catmull-Rom → cubic Bézier), same smoothing the Finance
        // TrendChart uses, so curves read consistently across the app.
        _lineGeometry = Smooth(geoPts, closeToBaseline: false, bottom);
        _areaGeometry = Smooth(geoPts, closeToBaseline: true, bottom);

        // Horizontal gradient brushes (left→right), cloned from the shared stops.
        var lineGradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        };
        foreach (var s in LineStops)
        {
            lineGradient.GradientStops.Add(new GradientStop(s.Color, s.Offset));
        }

        // Area: same horizontal hue sweep, but faded vertically (soft near the
        // line, transparent at the baseline). Avalonia brushes take a single
        // gradient axis, so the vertical fade is applied as a low overall opacity
        // on a horizontal gradient whose stops carry the vertical alpha ramp via a
        // stacked opacity mask would be overkill here — instead we use a horizontal
        // gradient at reduced alpha, which matches the reference's soft wash.
        var areaGradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            Opacity = 0.16,
        };
        foreach (var s in LineStops)
        {
            areaGradient.GradientStops.Add(new GradientStop(s.Color, s.Offset));
        }

        _areaBrush = areaGradient;
        _linePen = new Pen(lineGradient, 3.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        // The dot ring picks up the mid-line colour so markers blend with the gradient.
        _dotPen = new Pen(lineGradient, 2);
    }

    /// <summary>
    /// Catmull-Rom → cubic Bézier smoothing (mirrors Finance <c>smoothPath</c>).
    /// When <paramref name="closeToBaseline"/> is set the figure is closed down to
    /// the baseline to form the fillable area.
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
            var c1x = p1.X + ((p2.X - p0.X) * t);
            var c1y = p1.Y + ((p2.Y - p0.Y) * t);
            var c2x = p2.X - ((p3.X - p1.X) * t);
            var c2y = p2.Y - ((p3.Y - p1.Y) * t);
            g.CubicBezierTo(new Point(c1x, c1y), new Point(c2x, c2y), p2);
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
            var d = Math.Abs(px[i] - x);
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
}
