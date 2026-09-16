using System;
using System.Collections.Generic;
using Animora.Contracts.Dashboard;
using Avalonia;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Dashboard;

/// <summary>
/// A single coloured arc of the species donut, rendered as a stroked ring
/// segment. Mirrors the SVG donut segments built in
/// Animora-UI/assets/dashboard.js (a 120×120 viewBox, r=48, 13px ring), but
/// expressed as an Avalonia <see cref="Geometry"/> so it binds to a Path.
/// </summary>
public sealed class DonutSliceViewModel
{
    private const double Center = 60;
    private const double Radius = 48;
    private const double StrokeWidth = 13;

    // A tiny gap (in degrees) between slices, matching the reference's subtle
    // segment separation.
    private const double GapDegrees = 2.5;

    private DonutSliceViewModel(Geometry geometry, IBrush brush)
    {
        Geometry = geometry;
        Brush = brush;
        StrokeThickness = StrokeWidth;
    }

    public Geometry Geometry { get; }

    public IBrush Brush { get; }

    public double StrokeThickness { get; }

    public static IReadOnlyList<DonutSliceViewModel> Build(IReadOnlyList<DashboardSpecies> species)
    {
        var slices = new List<DonutSliceViewModel>(species.Count);
        var total = 0;
        foreach (var s in species)
        {
            total += s.Count;
        }

        if (total <= 0)
        {
            return slices;
        }

        // Start at the top (12 o'clock) and sweep clockwise, matching the
        // reference donut's orientation.
        var angle = -90.0;
        foreach (var s in species)
        {
            var sweep = s.Count / (double)total * 360.0;
            var start = angle + GapDegrees / 2.0;
            var end = angle + sweep - GapDegrees / 2.0;
            if (end > start)
            {
                slices.Add(new DonutSliceViewModel(BuildArc(start, end), ParseBrush(s.ColorHex)));
            }

            angle += sweep;
        }

        return slices;
    }

    private static Geometry BuildArc(double startDeg, double endDeg)
    {
        var p1 = PointOnCircle(startDeg);
        var p2 = PointOnCircle(endDeg);
        var isLargeArc = (endDeg - startDeg) > 180.0;

        var figure = new PathFigure
        {
            StartPoint = p1,
            IsClosed = false,
            IsFilled = false,
        };
        figure.Segments!.Add(new ArcSegment
        {
            Point = p2,
            Size = new Size(Radius, Radius),
            RotationAngle = 0,
            IsLargeArc = isLargeArc,
            SweepDirection = SweepDirection.Clockwise,
        });

        var geometry = new PathGeometry();
        geometry.Figures!.Add(figure);
        return geometry;
    }

    private static Point PointOnCircle(double angleDeg)
    {
        var rad = angleDeg * Math.PI / 180.0;
        return new Point(
            Center + Radius * Math.Cos(rad),
            Center + Radius * Math.Sin(rad));
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
