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

    /// <summary>
    /// Returns a shared <see cref="SolidColorBrush"/> for <paramref name="hex"/>
    /// with an explicit 8-bit <paramref name="alpha"/> applied — the
    /// alpha-aware sibling of <see cref="Solid"/> (mirrors callers that build a
    /// soft/tinted surface via <c>Color.FromArgb(alpha, …)</c>).
    /// <para>
    /// <b>Bounded key space (PERFORMANCE.md §15).</b> The colour comes from the
    /// fixed design-system palette and the alpha is an 8-bit value, so the
    /// number of distinct keys is finite and tiny. The alpha is part of the key,
    /// so callers with different transparency never share the wrong instance.
    /// Unparseable colours still yield a deterministic keyed entry (transparent
    /// composited with <paramref name="alpha"/>), so the space stays bounded.
    /// </para>
    /// </summary>
    public static IBrush Solid(string hex, byte alpha)
    {
        // Compose a bounded, parseable #AARRGGBB key so both the lookup and the
        // stored brush describe exactly the requested (colour, alpha) pair.
        var baseColor = Color.TryParse(hex, out var parsed) ? parsed : Colors.Transparent;
        var key = $"#{alpha:X2}{baseColor.R:X2}{baseColor.G:X2}{baseColor.B:X2}";

        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        // Reuse the opaque cache for readability when fully opaque.
        if (alpha == 0xFF)
        {
            return Cache.GetOrAdd(key, Solid(hex));
        }

        var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
        return Cache.GetOrAdd(key, brush);
    }
}
