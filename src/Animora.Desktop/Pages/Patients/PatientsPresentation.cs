using Animora.Contracts.Patients;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Avalonia;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Patients;

/// <summary>
/// Shared presentation mapping for the Patients &amp; Owners directory. Every
/// value here is transcribed from the screen's own CSS/JS in the reference
/// (Animora-UI/assets/patients.css + patients-ui.js + patients-data.js) — the
/// species taxonomy, the avatar/cover gradients, the sex-dot palette and the
/// status/tag tone maps — so the cards/rows can bind pre-resolved brushes and
/// strings without any UI logic leaking into the data contracts.
/// </summary>
public static class PatientsPresentation
{
    // ---- species (patients-data.js SPECIES) ----

    public static string SpeciesLabel(PetSpecies species) => species switch
    {
        PetSpecies.Cat => "گربه",
        PetSpecies.Dog => "سگ",
        PetSpecies.Bird => "پرنده",
        PetSpecies.Rabbit => "خرگوش",
        PetSpecies.Rodent => "جوندگان",
        _ => "سایر",
    };

    public static string SpeciesIconKey(PetSpecies species) => species switch
    {
        PetSpecies.Cat => "IconCat",
        PetSpecies.Dog => "IconDog",
        PetSpecies.Bird => "IconBird",
        PetSpecies.Rabbit => "IconRabbit",
        _ => "IconMore2", // rodent / other both use the "more2" glyph
    };

    /// <summary>
    /// Species cover gradient behind a pet card (patients-page.js COVERS), a
    /// 135° two-stop fill.
    /// </summary>
    public static IBrush CoverBrush(PetSpecies species)
    {
        var (from, to) = species switch
        {
            PetSpecies.Cat => ("#ece5fb", "#f8f5ff"),
            PetSpecies.Dog => ("#fbe7d6", "#fff7f0"),
            PetSpecies.Bird => ("#fffaeb", "#fffaf0"),
            PetSpecies.Rabbit => ("#fbe4f1", "#fff5fb"),
            PetSpecies.Rodent => ("#ddf0e9", "#f3fbf8"),
            _ => ("#eff8ff", "#f4f8ff"),
        };
        return Diagonal(from, to);
    }

    // ---- avatar / cover tones (patients.css .tone-*) ----

    public static IBrush ToneBrush(PatientAvatarTone tone)
    {
        var (from, to) = tone switch
        {
            PatientAvatarTone.A => ("#b8a2e6", "#8f74d1"),
            PatientAvatarTone.B => ("#f0b593", "#e08a5c"),
            PatientAvatarTone.C => ("#7ac7b6", "#4aa591"),
            PatientAvatarTone.D => ("#ea8f8f", "#d16060"),
            PatientAvatarTone.E => ("#8fb5f0", "#5c8ad6"),
            _ => ("#fcd97a", "#e0ad3a"),
        };
        return Diagonal(from, to);
    }

    // ---- status pill (patients-data.js STATUS_MAP) ----

    public static string StatusText(PetStatus status) => status switch
    {
        PetStatus.FollowUp => "در حال پیگیری",
        PetStatus.Critical => "بیمار مزمن",
        _ => "سالم",
    };

    public static Tone StatusTone(PetStatus status) => status switch
    {
        PetStatus.FollowUp => Tone.Warn,
        PetStatus.Critical => Tone.Danger,
        _ => Tone.Ok,
    };

    // ---- sex dot (patients-ui.js sexDot + patients.css .sex-dot) ----

    public static SexDotInfo SexDot(PetGender gender) => gender switch
    {
        PetGender.Male => new SexDotInfo("♂", "نر", Brush("#eff8ff"), Brush("#1570cd")),
        PetGender.Female => new SexDotInfo("♀", "ماده", Brush("#fbe4f1"), Brush("#a8478a")),
        _ => new SexDotInfo("؟", "نامشخص", Brush("#eef2f0"), Brush("#7a8b89")),
    };

    // ---- owner gender / tags / balance (patients-data.js + patients.css) ----

    public static string OwnerGenderLabel(OwnerGender gender) =>
        gender == OwnerGender.Male ? "آقا" : "خانم";

    /// <summary>Tag pill palette (patients.css .oc-tag.vip/.debt/.loyal).</summary>
    public static (IBrush Background, IBrush Foreground) TagTone(string tag) => tag switch
    {
        "VIP" => (Brush("#f4f3ff"), Brush("#7b5cc4")),
        "بدهکار" => (Brush("#fef3f2"), Brush("#d92d20")),
        "مشتری وفادار" => (Brush("#ecfdf3"), Brush("#027a48")),
        _ => (Brush("#eef2f0"), Brush("#536b68")),
    };

    /// <summary>Initials for an owner avatar (patients-ui.js initials).</summary>
    public static string Initials(string first, string last)
    {
        var a = string.IsNullOrEmpty(first) ? "؟" : first[..1];
        var b = string.IsNullOrEmpty(last) ? string.Empty : last[..1];
        return a + b;
    }

    // ---- primitives ----

    public sealed record SexDotInfo(string Glyph, string Title, IBrush Background, IBrush Foreground);

    /// <summary>
    /// Weight value for a card/row: the number in Persian digits ("۴.۴"), or the
    /// reference's em-dash placeholder when none is recorded.
    /// </summary>
    public static string WeightText(double? kg) => kg is { } value
        ? PersianNumberFormatter.ToPersianDigits(
            value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
        : "—";

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    private static IBrush Diagonal(string from, string to) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse(from), 0),
            new GradientStop(Color.Parse(to), 1),
        },
    };
}
