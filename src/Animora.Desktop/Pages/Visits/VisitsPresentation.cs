using System.Globalization;
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
        _ => Fill("#0e7566"),
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
        _ => (Fill("#f0fdfa"), Fill("#0e7566")),
    };

    /// <summary>A solid fill brush from a hex string.</summary>
    public static IBrush Fill(string hex) => new SolidColorBrush(Color.Parse(hex));

    /// <summary>A fill brush carrying an alpha channel over a hex color.</summary>
    public static IBrush FillAlpha(string hex, byte alpha)
    {
        var color = Color.Parse(hex);
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    /// <summary>
    /// Money formatter mirroring the reference's
    /// <c>money = (n) => toFa(Number(n).toLocaleString('en-US')) + ' ریال'</c>
    /// (comma-grouped digits, Persian digits, trailing «ریال»).
    /// </summary>
    public static string Money(long value) =>
        PersianNumberFormatter.ToPersianDigits(value.ToString("N0", CultureInfo.InvariantCulture))
        + " ریال";

    /// <summary>Top-to-bottom two-stop gradient, as the reference uses for cards/tabs.</summary>
    public static IBrush Vertical(string from, string to) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse(from), 0),
            new GradientStop(Color.Parse(to), 1),
        },
    };
}
