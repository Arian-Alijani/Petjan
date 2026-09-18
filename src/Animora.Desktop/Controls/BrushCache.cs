using System.Collections.Concurrent;
using Avalonia.Media;

namespace Animora.Desktop.Controls;

/// <summary>
/// Process-wide cache for immutable solid brushes keyed by their hex string.
/// <para>
/// The presentation helpers (Appointments/Boarding/Cashier/Cheques) and the
/// custom-drawn controls (charts, rich lines) previously built a fresh
/// <see cref="SolidColorBrush"/> via <c>Color.Parse</c> on every access — i.e.
/// on every item in a bindable list and on every render pass. A brush is
/// immutable and freely shareable, so allocating one per call is pure GC
/// pressure in the UI hot path (PERFORMANCE.md §10: reduce hot-path
/// allocation). This cache keeps a single instance per colour.
/// </para>
/// <para>
/// The dictionary is tiny, bounded (one entry per distinct hex colour in the
/// design system) and lock-free on reads, so lookups stay cheap while the
/// cache stays bounded (PERFORMANCE.md §15: caches must be bounded).
/// </para>
/// </summary>
public static class BrushCache
{
    private static readonly ConcurrentDictionary<string, IBrush> Cache = new();

    /// <summary>
    /// Returns a shared <see cref="SolidColorBrush"/> for <paramref name="hex"/>
    /// (e.g. "#0d9488"), allocating it once and reusing it thereafter. Falls
    /// back to a transparent brush for unparseable input rather than throwing
    /// from a render/binding path.
    /// </summary>
    public static IBrush Solid(string hex)
    {
        if (Cache.TryGetValue(hex, out var cached))
        {
            return cached;
        }

        IBrush brush;
        if (Color.TryParse(hex, out var color))
        {
            brush = new SolidColorBrush(color);
        }
        else
        {
            brush = Brushes.Transparent;
        }

        // ConcurrentDictionary.GetOrAdd keeps the first writer's value; an
        // occasional duplicate instance under a race is harmless and short-lived.
        return Cache.GetOrAdd(hex, brush);
    }
}
