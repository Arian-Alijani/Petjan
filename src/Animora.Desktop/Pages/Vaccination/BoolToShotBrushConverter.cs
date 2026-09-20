using System;
using System.Globalization;
using Animora.Desktop.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Vaccination;

/// <summary>
/// Resolves a boolean "active" flag to the timeline-dot stroke, mirroring the
/// reference's <c>.vx-tl-item:not(.muted)::before</c> (the vaccine accent
/// violet) versus <c>.vx-tl-item.muted::before</c> (the neutral hairline
/// <c>--line</c>, transcription of <c>#e5eaee</c>). Keeping the two colors in
/// one converter lets the timeline bind a plain <c>IsActive</c> boolean without
/// duplicating the dot markup per row.
/// </summary>
public sealed class BoolToShotBrushConverter : IValueConverter
{
    /// <summary>The active dot stroke — the reference's <c>--vx-shot</c> violet.</summary>
    private static readonly IBrush ActiveBrush = BrushCache.Solid("#6f57c9");

    /// <summary>The muted dot stroke — the reference's <c>--line</c> hairline.</summary>
    private static readonly IBrush MutedBrush = BrushCache.Solid("#e5eaee");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? ActiveBrush : MutedBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
