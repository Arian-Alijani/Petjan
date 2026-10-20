using System;
using System.Collections.Generic;
using System.Globalization;
using Animora.Desktop.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Animora.Desktop.Controls;

/// <summary>One bar of a <see cref="BarChart"/> (label = short axis text, full = tooltip text).</summary>
public sealed record BarChartPoint(double Value, string Label, string Full);

/// <summary>
/// A lightweight single-series vertical bar chart (sales per day, visits per day): dashed gridlines with right-side value
/// labels, rounded bars in <see cref="BarBrush"/>, first/middle/last axis labels and a per-bar tooltip. RTL-aware (the
/// series is plotted oldest at the right edge, like <see cref="TrendChart"/>). Drawn with Avalonia primitives.
/// </summary>
public sealed class BarChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<BarChartPoint>?> PointsProperty =
        AvaloniaProperty.Register<BarChart, IReadOnlyList<BarChartPoint>?>(nameof(Points));

    public static readonly StyledProperty<IBrush> BarBrushProperty =
        AvaloniaProperty.Register<BarChart, IBrush>(nameof(BarBrush), new SolidColorBrush(Color.Parse("#4a6cf7")));

    /// <summary>True: the value axis uses short money labels («۲ م»); false: plain Persian counts.</summary>
    public static readonly StyledProperty<bool> IsMoneyProperty =
        AvaloniaProperty.Register<BarChart, bool>(nameof(IsMoney), true);

    static BarChart() => AffectsRender<BarChart>(PointsProperty, BarBrushProperty, IsMoneyProperty);

    private static readonly IBrush GridBrush = new SolidColorBrush(Color.Parse("#eef1f8"));
    private static readonly IBrush AxisBrush = new SolidColorBrush(Color.Parse("#98a2b3"));
    private static readonly Pen GridPen = new(GridBrush, 1, new DashStyle([2, 6], 0));

    private const double PadT = 14, PadB = 26, PadL = 10, PadR = 46;

    public IReadOnlyList<BarChartPoint>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public IBrush BarBrush
    {
        get => GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    public bool IsMoney
    {
        get => GetValue(IsMoneyProperty);
        set => SetValue(IsMoneyProperty, value);
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var iw = w - PadL - PadR;
        var ih = h - PadT - PadB;
        if (iw <= 1 || ih <= 1) return;

        var pts = Points;
        if (pts is null || pts.Count == 0) return;

        var maxV = 1.0;
        foreach (var p in pts) maxV = Math.Max(maxV, p.Value);
        maxV *= 1.1;

        var typeface = Typeface.Default;
        for (var i = 0; i <= 4; i++)
        {
            var gy = PadT + (ih * i / 4);
            ctx.DrawLine(GridPen, new Point(PadL, gy), new Point(w - PadR, gy));
            var label = new FormattedText(FormatAxis(maxV * (1 - (i / 4.0))), CultureInfo.InvariantCulture,
                FlowDirection.RightToLeft, typeface, 9.5, AxisBrush);
            ctx.DrawText(label, new Point(w - PadR + 8, gy + 3 - (label.Height / 2)));
        }

        var n = pts.Count;
        var slot = iw / n;
        var barW = Math.Clamp(slot * 0.62, 2, 34);
        var radius = Math.Min(4, barW / 2);
        var baseY = PadT + ih;
        for (var i = 0; i < n; i++)
        {
            // Oldest bucket at the right edge (RTL), newest at the left.
            var cx = w - PadR - (slot * (i + 0.5));
            var bh = Math.Max(pts[i].Value <= 0 ? 0 : 2, pts[i].Value / maxV * ih);
            if (bh <= 0) continue;
            ctx.DrawRectangle(BarBrush, null, new RoundedRect(new Rect(cx - (barW / 2), baseY - bh, barW, bh), radius));
        }

        foreach (var i in AxisIndexes(n))
        {
            var cx = w - PadR - (slot * (i + 0.5));
            var label = new FormattedText(pts[i].Label, CultureInfo.InvariantCulture, FlowDirection.RightToLeft,
                typeface, 9.5, AxisBrush);
            ctx.DrawText(label, new Point(cx - (label.Width / 2), baseY + 7));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pts = Points;
        var iw = Bounds.Width - PadL - PadR;
        if (pts is null || pts.Count == 0 || iw <= 1) return;
        var fromRight = (Bounds.Width - PadR) - e.GetPosition(this).X;
        var index = (int)Math.Floor(fromRight / (iw / pts.Count));
        ToolTip.SetTip(this, index >= 0 && index < pts.Count ? pts[index].Full : null);
    }

    private static IEnumerable<int> AxisIndexes(int n)
    {
        if (n <= 3)
        {
            for (var i = 0; i < n; i++) yield return i;
            yield break;
        }

        yield return 0;
        yield return n / 2;
        yield return n - 1;
    }

    private string FormatAxis(double v) => IsMoney
        ? TrendChart.ShortMoney(v)
        : PersianNumberFormatter.ToPersianDigits(Math.Round(v).ToString("0", CultureInfo.InvariantCulture));
}
