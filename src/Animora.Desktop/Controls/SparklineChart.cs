using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Animora.Desktop.Controls;

/// <summary>
/// Minimal KPI sparkline: a straight-segment line (sharp zig-zag, like the reference) with a faint vertical area
/// fade underneath and an end-point dot. No axes, grid, labels or hover — it is decoration for a StatCard, not a
/// data-reading chart (the full revenue chart is <see cref="RevenuePulseChart"/>).
///
/// <para>Input is the series already normalized to 0..1 (oldest first) by the client service; values are clamped
/// defensively and a non-finite value is drawn as 0, so a bad point can never produce NaN geometry. Fewer than two
/// points draws nothing. Geometry is rebuilt only when the points or the control size change, never per frame
/// (PERFORMANCE.md §10/§11).</para>
/// </summary>
public sealed class SparklineChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> PointsProperty =
        AvaloniaProperty.Register<SparklineChart, IReadOnlyList<double>?>(nameof(Points));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<SparklineChart, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<SparklineChart, double>(nameof(StrokeThickness), 2);

    // Room so the stroke, round joins and the end dot are never clipped at the control edge.
    private const double Inset = 4;
    private const double DotRadius = 3;

    private StreamGeometry? _line;
    private StreamGeometry? _area;
    private Point _end;
    private Size _builtFor;
    private IReadOnlyList<double>? _builtPoints;
    private IBrush? _areaBrush;
    private IBrush? _areaBrushSource;
    private Pen? _pen;

    static SparklineChart()
    {
        AffectsRender<SparklineChart>(PointsProperty, StrokeProperty, StrokeThicknessProperty);
    }

    /// <summary>Series in 0..1, oldest first.</summary>
    public IReadOnlyList<double>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    /// <summary>Line colour; the area fade is derived from it when it is a solid colour.</summary>
    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var points = Points;
        var stroke = Stroke;
        if (points is null || points.Count < 2 || stroke is null) return;
        var size = Bounds.Size;
        if (size.Width <= Inset * 2 || size.Height <= Inset * 2) return;

        if (_line is null || _builtFor != size || !ReferenceEquals(_builtPoints, points)) Rebuild(points, size);
        // Rebuild always assigns both geometries together; capture them as locals so the compiler
        // can prove non-null at the draw calls below (and so a draw can never see a half-built state).
        var line = _line;
        var area = _area;
        if (line is null) return;

        var pen = _pen;
        if (pen is null || !ReferenceEquals(pen.Brush, stroke) || pen.Thickness != StrokeThickness)
            _pen = pen = new Pen(stroke, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        if (!ReferenceEquals(_areaBrushSource, stroke))
        {
            _areaBrushSource = stroke;
            _areaBrush = BuildAreaBrush(stroke);
        }

        if (_areaBrush is not null && area is not null) context.DrawGeometry(_areaBrush, null, area);
        context.DrawGeometry(null, pen, line);
        context.DrawEllipse(Brushes.White, pen, _end, DotRadius, DotRadius);
    }

    private void Rebuild(IReadOnlyList<double> points, Size size)
    {
        var left = Inset;
        var top = Inset;
        var width = size.Width - Inset * 2;
        var height = size.Height - Inset * 2;
        var step = width / (points.Count - 1);
        Point At(int i)
        {
            var v = points[i];
            if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
            return new Point(left + i * step, top + (1 - Math.Clamp(v, 0, 1)) * height);
        }

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(At(0), false);
            for (var i = 1; i < points.Count; i++) ctx.LineTo(At(i));
            ctx.EndFigure(false);
        }

        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(left, top + height), true);
            for (var i = 0; i < points.Count; i++) ctx.LineTo(At(i));
            ctx.LineTo(new Point(left + width, top + height));
            ctx.EndFigure(true);
        }

        _line = line;
        _area = area;
        _end = At(points.Count - 1);
        _builtFor = size;
        _builtPoints = points;
    }

    // Vertical fade of the line colour: a faint wash under the peaks melting to fully transparent at the baseline.
    private static IBrush? BuildAreaBrush(IBrush stroke)
    {
        if (stroke is not ISolidColorBrush solid) return null;
        var c = solid.Color;
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x2E, c.R, c.G, c.B), 0.0),
                new GradientStop(Color.FromArgb(0x00, c.R, c.G, c.B), 1.0),
            },
        };
    }
}
