namespace Animora.Client.Services.Appointments;

/// <summary>
/// Fixed, curated accent palette for appointment types and doctors. Colors are assigned once
/// (by catalog/roster order or when a custom type is created) so they are stable across runs and
/// never re-generated per render.
/// </summary>
internal static class AppointmentColorPalette
{
    /// <summary>Mid-tone hues readable with both white and dark text on their tints.</summary>
    internal static readonly string[] Accents =
    [
        "#0D9488", "#1570CD", "#8F74D1", "#D92D20", "#E08A5C", "#2F9FB0", "#B54708", "#5A67D8",
        "#C026D3", "#16A34A", "#DB2777", "#CA8A04", "#0891B2", "#7C3AED", "#EA580C", "#4D7C0F",
    ];

    /// <summary>Accent for the n-th item of a fixed ordered set (wraps when exhausted).</summary>
    internal static string ByIndex(int index) => Accents[((index % Accents.Length) + Accents.Length) % Accents.Length];

    /// <summary>Stable (process-independent) hash pick, for items without a natural index.</summary>
    internal static string ByKey(string key)
    {
        uint hash = 2166136261;
        foreach (var ch in key) hash = (hash ^ ch) * 16777619;
        return Accents[(int)(hash % (uint)Accents.Length)];
    }

    /// <summary>
    /// A palette color not in <paramref name="used"/>; when every palette color is taken, a random
    /// saturated color is generated (the caller persists it once).
    /// </summary>
    internal static string PickUnused(IEnumerable<string> used)
    {
        var taken = new HashSet<string>(used, StringComparer.OrdinalIgnoreCase);
        foreach (var accent in Accents)
            if (!taken.Contains(accent)) return accent;

        for (var attempt = 0; attempt < 32; attempt++)
        {
            var candidate = FromHue(Random.Shared.Next(0, 360));
            if (!taken.Contains(candidate)) return candidate;
        }
        return FromHue(Random.Shared.Next(0, 360));
    }

    private static string FromHue(int hue)
    {
        // HSL(hue, 62%, 42%) → RGB.
        const double s = 0.62, l = 0.42;
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(hue / 60.0 % 2 - 1));
        var m = l - c / 2;
        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        static int B(double v) => (int)Math.Round(v * 255);
        return $"#{B(r + m):X2}{B(g + m):X2}{B(b + m):X2}";
    }
}
