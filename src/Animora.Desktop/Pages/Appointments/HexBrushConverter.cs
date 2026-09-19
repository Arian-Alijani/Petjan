using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Appointments;

/// <summary>
/// Converts an HTML hex color string (e.g. "#0d9488") to a
/// <see cref="SolidColorBrush"/> so a data-driven list (type/doctor filter
/// chips, accent dots) can bind a per-item accent without a per-item resource.
/// Returns <c>null</c> for empty/invalid input so the bound property keeps its
/// default.
/// </summary>
public sealed class HexBrushConverter : IValueConverter
{
    public static HexBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            // Shared, cached brush — this converter runs once per bound chip /
            // accent dot, so allocating per call is avoidable GC churn.
            return Animora.Desktop.Controls.BrushCache.Solid(hex);
        }

        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
