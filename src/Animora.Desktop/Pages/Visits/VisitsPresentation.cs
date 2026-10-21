using System.Globalization;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Avalonia;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Visits;

/// <summary>
/// Shared presentation mapping for the Visits triage gate. Every value here is
/// transcribed from the screen's own CSS/JS in the reference
/// (Animora-UI/visits.html + assets/visit.js + assets/clinic-data.js) — the
/// pet-icon classes, the species avatar palettes and the visit-type accents —
/// so the cards/rows can bind pre-resolved brushes and icon keys without any
/// UI logic leaking into the data contracts.
/// </summary>
public static class VisitsPresentation
{
    /// <summary>
    /// Maps the free-text kind a receptionist types for a new walk-in patient (Persian or English,
    /// e.g. «سگ», «پرشین», "Cat") to the <c>ic</c> discriminator the avatar helpers expect (V21).
    /// </summary>
    public static string IconKeyForKind(string? kind)
    {
        var k = (kind ?? string.Empty).Trim().ToLowerInvariant();
        if (k.Length == 0) return "paw";
        if (HasAny(k, "خرگوش", "rabbit")) return "rabbit";
        if (HasAny(k, "گربه", "cat", "kitten", "پرشین", "اسکاتیش", "بنگال", "رگدال")) return "cat";
        if (HasAny(k, "پرنده", "bird", "طوطی", "کاناری", "کوکتیل", "مرغ")) return "bird";
        if (HasAny(k, "سگ", "dog", "puppy", "ژرمن", "هاسکی", "پاگ", "گلدن", "تریر", "پودل")) return "dog";
        return "paw";
    }

    private static bool HasAny(string value, params string[] needles) => needles.Any(value.Contains);

    /// <summary>
    /// Pet glyph key for a visit's <c>ic</c> discriminator, mirroring the
    /// reference's <c>petIcClass</c> (falls back to the cat glyph for any
    /// unrecognised species).
    /// </summary>
    public static string PetIconKey(string? ic) => ic switch
    {
        "dog" => "IconDog",
        "bird" => "IconBird",
        "rabbit" => "IconRabbit",
        "cat" => "IconCat",
        _ => "IconPaw",
    };

    /// <summary>
    /// Booked-row pet avatar tile fill + glyph color, mirroring the
    /// reference's <c>.appt-row .avpet(.dog|.cat|.bird|.rabbit)</c> palette
    /// (default is the lilac tile).
    /// </summary>
    public static (IBrush Background, IBrush Foreground) PetAvatar(string? ic) => ic switch
    {
        "dog" => (Fill("#dbeafe"), Fill("#3057a6")),
        "cat" => (Fill("#fdf1e7"), Fill("#d47a3f")),
        "bird" => (Fill("#d9f4ea"), Fill("#2a7f5f")),
        "rabbit" => (Fill("#fde5f0"), Fill("#b6448a")),
        _ => (Fill("#f4f3ff"), Fill("#7b5cc4")),
    };

    /// <summary>
    /// Selected-patient glyph color, mirroring <c>.patient-picked .av.(cat|dog|
    /// bird|rabbit)</c> (tile stays white with a brand hairline).
    /// </summary>
    public static IBrush PatientPickedGlyph(string? ic) => ic switch
    {
        "cat" => Fill("#d47a3f"),
        "dog" => Fill("#3057a6"),
        "bird" => Fill("#2a7f5f"),
        "rabbit" => Fill("#b6448a"),
        _ => Fill("#3a56d4"),
    };

    /// <summary>
    /// The wizard patient sidebar's large avatar tile fill + glyph color,
    /// mirroring <c>.wz-patient .av-lg(.dog|.cat|.bird|.rabbit)</c>
    /// (default is the brand wash with the brand-700 glyph).
    /// </summary>
    public static (IBrush Background, IBrush Foreground) WizardAvatar(string? ic) => ic switch
    {
        "dog" => (Fill("#e4edfa"), Fill("#3057a6")),
        "cat" => (Fill("#fdf1e7"), Fill("#d47a3f")),
        "bird" => (Fill("#d9f4ea"), Fill("#2a7f5f")),
        "rabbit" => (Fill("#fde5f0"), Fill("#b6448a")),
        _ => (Fill("#eef2fe"), Fill("#3a56d4")),
    };

    /// <summary>A solid fill brush from a hex string (shared via <see cref="BrushCache"/>).</summary>
    public static IBrush Fill(string hex) => BrushCache.Solid(hex);

    /// <summary>
    /// A fill brush carrying an alpha channel over a hex color, shared via
    /// <see cref="BrushCache"/> — the (colour, alpha) pair comes from the fixed
    /// palette, so the key space stays bounded (PERFORMANCE.md §15).
    /// </summary>
    public static IBrush FillAlpha(string hex, byte alpha) => BrushCache.Solid(hex, alpha);

    /// <summary>
    /// Money formatter mirroring the reference's
    /// <c>money = (n) => toFa(Number(n).toLocaleString('en-US')) + ' ریال'</c>
    /// (comma-grouped digits, Persian digits, trailing «ریال»).
    /// </summary>
    public static string Money(long value) =>
        PersianNumberFormatter.ToPersianDigits(value.ToString("N0", CultureInfo.InvariantCulture))
        + " ریال";

    /// <summary>
    /// Top-to-bottom two-stop gradient, as the reference uses for cards/tabs.
    /// Shared via <see cref="GradientBrushCache"/> — the callers pass fixed
    /// pairs, so no brush is allocated per call (PERFORMANCE.md §10).
    /// </summary>
    public static IBrush Vertical(string from, string to) =>
        GradientBrushCache.LinearGradient(from, to, 0, 0, 0, 1);
}
