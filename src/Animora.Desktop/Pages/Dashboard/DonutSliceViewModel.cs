using System;
using System.Collections.Generic;
using Animora.Contracts.Dashboard;
using Animora.Desktop.Controls;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Dashboard;

/// <summary>
/// A single coloured arc of the species donut, rendered as a stroked ring
/// segment. Mirrors the SVG donut segments built in
/// Animora-UI/assets/dashboard.js (a 120×120 viewBox, r=48, 13px ring), but
/// expressed as an Avalonia <see cref="Geometry"/> so it binds to a Path.
/// Carries its slice <see cref="Index"/> so the ring and the legend can
/// cross-highlight, plus the hover dim/focus state (reference
/// <c>.donut-wrap.dim</c>/<c>.seg.on</c>).
/// </summary>
public sealed partial class DonutSliceViewModel : ObservableObject
{
    private const double Center = 60;
    private const double Radius = 48;
    private const double StrokeWidth = 13;

    // Hovered/active slice grows to SW + 4, matching the reference's
    // `.donut-wrap .seg.on { stroke-width: SW + 4 }` growth on the ring.
    private const double ActiveStrokeWidth = StrokeWidth + 4;

    // The reference separates slices by a GAP arc-length of 3.4 on an r=48
    // ring; convert that to degrees so the gap is identical in size.
    private const double GapDegrees = 3.4 / Radius * (180.0 / Math.PI);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StrokeThickness))]
    [NotifyPropertyChangedFor(nameof(Opacity))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Opacity))]
    private bool _isDimmed;

    private DonutSliceViewModel(int index, string colorHex, Geometry geometry, IBrush brush)
    {
        Index = index;
        ColorHex = colorHex;
        Geometry = geometry;
        Brush = brush;
    }

    /// <summary>Position of this slice in the ring (matches the legend row).</summary>
    public int Index { get; }

    public string ColorHex { get; }

    public Geometry Geometry { get; }

    public IBrush Brush { get; }

    /// <summary>Ring stroke width — grows on the active/hovered slice.</summary>
    public double StrokeThickness => IsActive ? ActiveStrokeWidth : StrokeWidth;

    /// <summary>Dims the slice while another is focused (reference <c>.dim .seg</c>).</summary>
    public double Opacity => IsDimmed && !IsActive ? 0.22 : 1;

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
        for (var i = 0; i < species.Count; i++)
        {
            var s = species[i];
            var sweep = s.Count / (double)total * 360.0;
            var start = angle + GapDegrees / 2.0;
            var end = angle + sweep - GapDegrees / 2.0;
            if (end > start)
            {
                slices.Add(new DonutSliceViewModel(i, s.ColorHex, BuildArc(start, end), ParseBrush(s.ColorHex)));
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

    private static IBrush ParseBrush(string hex) =>
        // Shared, cached solid brush (PERFORMANCE.md §10); the species colours
        // are a fixed palette, so this never allocates per slice. Preserves the
        // previous invalid-input fallback to Gray.
        Color.TryParse(hex, out _) ? BrushCache.Solid(hex) : Brushes.Gray;
}
