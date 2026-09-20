using System;
using System.Collections.Concurrent;
using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace Animora.Desktop.Controls;

/// <summary>
/// Process-wide cache for immutable linear-gradient brushes, the gradient
/// sibling of <see cref="BrushCache"/>.
/// <para>
/// The presentation helpers (Boarding/Appointments/Cheques) previously built a
/// fresh <see cref="LinearGradientBrush"/> — parsing every stop colour via
/// <c>Color.Parse</c> — on every access, i.e. once per row/item/render pass.
/// A gradient brush is immutable and freely shareable, so allocating one per
/// call is pure GC pressure in the UI hot path (PERFORMANCE.md §10: reduce
/// hot-path allocation). This cache keeps a single frozen instance per distinct
/// parameter combination.
/// </para>
/// <para>
/// <b>Bounded key space (PERFORMANCE.md §15).</b> Every gradient in this app is
/// derived from a small, fixed design-system palette (the ~8 avatar colours,
/// the handful of bank/progress tone pairs and the semantic accents) combined
/// with a handful of fixed geometries (diagonal 135°, horizontal 90°, vertical
/// fade). The colour arguments therefore come from a closed set, and the
/// direction arguments from a closed set, so the number of distinct keys is
/// finite and tiny — it cannot grow without limit over a long session. The one
/// input that is <em>not</em> naturally discrete — a progress percentage — is
/// quantised to a whole percent (0..100) by <see cref="ProgressBar"/> before it
/// reaches the key, capping that dimension at 101 values. As with
/// <see cref="BrushCache"/>, reads are lock-free (<see cref="ConcurrentDictionary{TKey,TValue}"/>).
/// </para>
/// </summary>
public static class GradientBrushCache
{
    // Keyed by a compact string built from every parameter that affects the
    // rendered brush (geometry endpoints + ordered stops). Distinct visual
    // output ⇒ distinct key; identical parameters ⇒ one shared instance.
    private static readonly ConcurrentDictionary<string, IBrush> TwoStopCache = new();
    private static readonly ConcurrentDictionary<string, IBrush> ProgressCache = new();

    /// <summary>
    /// Returns a shared, immutable two-stop <see cref="LinearGradientBrush"/>
    /// running from (<paramref name="x1"/>,<paramref name="y1"/>) to
    /// (<paramref name="x2"/>,<paramref name="y2"/>) in relative coordinates,
    /// with <paramref name="from"/> at offset 0 and <paramref name="to"/> at
    /// offset 1. Allocated once per distinct parameter set and reused
    /// thereafter. Unparseable colours fall back to transparent rather than
    /// throwing from a render/binding path.
    /// </summary>
    public static IBrush LinearGradient(
        string from,
        string to,
        double x1,
        double y1,
        double x2,
        double y2)
    {
        var key = Key(from, to, x1, y1, x2, y2);
        if (TwoStopCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(x1, y1, RelativeUnit.Relative),
            EndPoint = new RelativePoint(x2, y2, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Parse(from), 0),
                new GradientStop(Parse(to), 1),
            },
        };

        // Freeze so the shared instance can never be mutated by a caller and is
        // safe to hand to any number of visuals (immutability = shareable).
        return TwoStopCache.GetOrAdd(key, brush.ToImmutable());
    }

    /// <summary>
    /// Returns a shared, immutable four-stop progress-bar
    /// <see cref="LinearGradientBrush"/>: a <paramref name="from"/>→
    /// <paramref name="to"/> fill from the inline-start (right, RTL) edge up to
    /// <paramref name="percent"/>%, then the <paramref name="track"/> colour for
    /// the remainder — matching the reference's block <c>i</c> over its track.
    /// <para>
    /// <paramref name="percent"/> is quantised to a whole percent in 0..100
    /// before it enters the cache key, so this dimension of the key space is
    /// capped at 101 values (PERFORMANCE.md §15) — a fractional percent would
    /// otherwise make the key space unbounded. The one-percent granularity is
    /// visually indistinguishable on a progress bar.
    /// </para>
    /// </summary>
    public static IBrush ProgressBar(string from, string to, string track, int percent)
    {
        // Quantise to a bounded 0..100 domain before keying (see §15 above).
        var pct = Math.Clamp(percent, 0, 100);
        var frac = pct / 100d;

        var key = string.Concat(
            from, "|", to, "|", track, "|", pct.ToString(CultureInfo.InvariantCulture));
        if (ProgressCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var brush = new LinearGradientBrush
        {
            // Inline-start (right) → inline-end (left), as the RTL bar fills.
            StartPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Parse(from), 0),
                new GradientStop(Parse(to), frac),
                new GradientStop(Parse(track), frac),
                new GradientStop(Parse(track), 1),
            },
        };

        return ProgressCache.GetOrAdd(key, brush.ToImmutable());
    }

    private static string Key(string from, string to, double x1, double y1, double x2, double y2) =>
        string.Concat(
            from, "|", to, "|",
            x1.ToString(CultureInfo.InvariantCulture), ",",
            y1.ToString(CultureInfo.InvariantCulture), ",",
            x2.ToString(CultureInfo.InvariantCulture), ",",
            y2.ToString(CultureInfo.InvariantCulture));

    private static Color Parse(string hex) =>
        Color.TryParse(hex, out var color) ? color : Colors.Transparent;
}
