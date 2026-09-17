using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Animora.Desktop.Controls;

/// <summary>One point on a <see cref="WeightChart"/> (label = full Jalali date, short = axis label).</summary>
public sealed record WeightChartPoint(string Label, string ShortLabel, double Value);

/// <summary>
/// Line + area growth chart for the pet file's weight panels, re-drawn from the
/// reference's SVG <c>lineChart</c> (Animora-UI/assets/patients-ui.js): 4 dashed
/// gridlines with Persian-digit Y labels, a soft brand gradient area under the
/// line, a 2.4px brand line, white-filled dots, and short X-axis labels. Drawn
/// with Avalonia drawing primitives (per CONVENTIONS §4a rule 2), not embedded SVG.
/// </summary>
public sealed class WeightChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<WeightChartPoint>?> PointsProperty =
        AvaloniaProperty.Register<WeightChart, IReadOnlyList<WeightChartPoint>?>(nameof(Points));

    public static readonly StyledProperty<IBrush> LineBrushProperty =
        AvaloniaProperty.Register<WeightChart, IBrush>(nameof(LineBrush),
            new SolidColorBrush(Color.Parse("#0d9488")));

    static WeightChart()
    {
        AffectsRender<WeightChart>(PointsProperty, LineBrushProperty);
    }

    /// <summary>Chart points (already Jalali-formatted). Null/empty → the reference's "no data" text.</summary>
    public IReadOnlyList<WeightChartPoint>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    /// <summary>Line/dot colour (reference brand-600 by default).</summary>
    public IBrush LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
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

        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#eef2f5")), 1);
        var axisBrush = new SolidColorBrush(Color.Parse("#98a2b3"));
        var typeface = new Typeface(FontFamily.Default);
        const double fontSize = 9;

        if (points is null || points.Count == 0)
        {
            var empty = new FormattedText(
                "داده‌ای ثبت نشده است", CultureInfo.InvariantCulture, FlowDirection.RightToLeft,
                typeface, 12, new SolidColorBrush(Color.Parse("#667085")));
            ctx.DrawText(empty, new Point((w - empty.Width) / 2, h / 2 - empty.Height / 2));
            return;
        }

        // pad mirrors the reference: { t:14, r:10, b:26, l:40 }
        const double padT = 14, padR = 10, padB = 26, padL = 40;
        var iw = w - padL - padR;
        var ih = h - padT - padB;

        var min = double.MaxValue;
        var max = double.MinValue;
        foreach (var p in points)
        {
            min = System.Math.Min(min, p.Value);
            max = System.Math.Max(max, p.Value);
        }

        var span = (max - min) > 0 ? (max - min) : 1;
        min = System.Math.Max(0, min - (span * 0.22));
        max += span * 0.22;
        var range = max - min;

        double X(int i) => padL + (points.Count <= 1 ? iw / 2 : (iw * i) / (points.Count - 1));
        double Y(double v) => padT + ih - (((v - min) / range) * ih);

        // gridlines + Y axis labels
        const int ticks = 4;
        for (var i = 0; i <= ticks; i++)
        {
            var v = min + (range * i / ticks);
            var y = Y(v);
            var pen = i == 0 ? gridPen : new Pen(gridPen.Brush, 1, new DashStyle([3, 4], 0));
            ctx.DrawLine(pen, new Point(padL, y), new Point(w - padR, y));

            var label = new FormattedText(
                PersianLabel(v), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, fontSize, axisBrush);
            ctx.DrawText(label, new Point(padL - 6 - label.Width, y - (label.Height / 2)));
        }

        // area under the line (brand gradient)
        var areaGeometry = new StreamGeometry();
        using (var g = areaGeometry.Open())
        {
            g.BeginFigure(new Point(X(0), padT + ih), true);
            for (var i = 0; i < points.Count; i++)
            {
                g.LineTo(new Point(X(i), Y(points[i].Value)));
            }

            g.LineTo(new Point(X(points.Count - 1), padT + ih));
            g.EndFigure(true);
        }

        var areaBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(((SolidColorBrush)LineBrush).Color, 0.20),
                new GradientStop(((SolidColorBrush)LineBrush).Color, 0),
            },
        };
        ctx.DrawGeometry(areaBrush, null, areaGeometry);

        // line
        var lineGeometry = new StreamGeometry();
        using (var g = lineGeometry.Open())
        {
            g.BeginFigure(new Point(X(0), Y(points[0].Value)), false);
            for (var i = 1; i < points.Count; i++)
            {
                g.LineTo(new Point(X(i), Y(points[i].Value)));
            }

            g.EndFigure(false);
        }

        ctx.DrawGeometry(null, new Pen(LineBrush, 2.4), lineGeometry);

        // dots
        var dotFill = new SolidColorBrush(Colors.White);
        var dotPen = new Pen(LineBrush, 2.2);
        for (var i = 0; i < points.Count; i++)
        {
            ctx.DrawEllipse(dotFill, dotPen, new Point(X(i), Y(points[i].Value)), 4, 4);
        }

        // X axis labels (thin out when crowded, like the reference)
        var step = points.Count > 6 ? (int)System.Math.Ceiling(points.Count / 6.0) : 1;
        for (var i = 0; i < points.Count; i++)
        {
            if (points.Count > 6 && i % step != 0 && i != points.Count - 1)
            {
                continue;
            }

            var t = new FormattedText(
                points[i].ShortLabel, CultureInfo.InvariantCulture, FlowDirection.RightToLeft,
                typeface, fontSize, axisBrush);
            ctx.DrawText(t, new Point(X(i) - (t.Width / 2), h - 6 - t.Height));
        }
    }

    private static string PersianLabel(double v)
    {
        var text = v < 10 ? v.ToString("0.#", CultureInfo.InvariantCulture)
                          : v.ToString("0", CultureInfo.InvariantCulture);
        return Localization.PersianNumberFormatter.ToPersianDigits(text);
    }
}
