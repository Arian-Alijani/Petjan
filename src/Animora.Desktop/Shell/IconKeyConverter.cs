using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Animora.Desktop.Shell;

/// <summary>
/// Resolves an icon resource key (e.g. "IconDashboard") to its
/// <see cref="Geometry"/> from application resources, so a data-driven list
/// (the sidebar) can bind icons by key without a per-item StaticResource.
/// </summary>
public sealed class IconKeyConverter : IValueConverter
{
    public static IconKeyConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key
            && global::Avalonia.Application.Current is { } app
            && app.TryFindResource(key, out var resource)
            && resource is Geometry geometry)
        {
            return geometry;
        }

        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
