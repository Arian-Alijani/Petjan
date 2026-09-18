using System.Globalization;
using Animora.Contracts.Boarding;
using Animora.Desktop.Localization;
using Avalonia;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Boarding;

/// <summary>
/// Presentation adapters for the Boarding screen: species labels/icon keys,
/// the avatar tone palette and the money formatter — all transcribed from the
/// reference (Animora-UI/assets/boarding.js <c>SPECIES</c>, the
/// <c>.bd-card-avatar.(a..f)</c> gradients in <c>boarding.css</c>, and the
/// <c>money()</c> helper). Kept here so the ViewModel and View stay free of
/// hardcoded literals.
/// </summary>
public static class BoardingPresentation
{
    /// <summary>
    /// The Persian species label, mirroring the reference's <c>SPECIES[sp].label</c>.
    /// </summary>
    public static string SpeciesLabel(BoardingSpecies species) => species switch
    {
        BoardingSpecies.Dog => "سگ",
        BoardingSpecies.Cat => "گربه",
        BoardingSpecies.Bird => "پرنده",
        BoardingSpecies.Rabbit => "خرگوش",
        BoardingSpecies.Rodent => "جونده",
        _ => "سایر",
    };

    /// <summary>
    /// The species icon resource key, mirroring the reference's
    /// <c>SPECIES[sp].icon</c> (dog/cat/bird/rabbit, falling back to the paw
    /// glyph for rodent/other).
    /// </summary>
    public static string SpeciesIconKey(BoardingSpecies species) => species switch
    {
        BoardingSpecies.Dog => "IconDog",
        BoardingSpecies.Cat => "IconCat",
        BoardingSpecies.Bird => "IconBird",
        BoardingSpecies.Rabbit => "IconRabbit",
        _ => "IconPaw",
    };

    /// <summary>
    /// The stay-card avatar fill, mirroring the reference's
    /// <c>.bd-card-avatar.(a..f)</c> two-stop 135° gradients (boarding.css).
    /// The gradient runs top-left → bottom-right, so the start point is (0,0)
    /// and the end point (1,1).
    /// </summary>
    public static IBrush AvatarBrush(string? tone)
    {
        var (from, to) = tone switch
        {
            "a" => ("#b8a2e6", "#8f74d1"),
            "b" => ("#f0b593", "#e08a5c"),
            "c" => ("#fcd97a", "#eab749"),
            "d" => ("#ea8f8f", "#d16060"),
            "e" => ("#7ac7b6", "#4aa591"),
            "f" => ("#8fb5f0", "#5c8ad6"),
            _ => ("#b8a2e6", "#8f74d1"),
        };

        return new LinearGradientBrush
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

    /// <summary>
    /// The stay-card avatar glyph colour, mirroring the reference's per-tone
    /// <c>color</c> (the amber «c» tone draws dark ink, all others white).
    /// </summary>
    public static IBrush AvatarGlyphBrush(string? tone) =>
        tone == "c" ? Fill("#101828") : Fill("#ffffff");

    /// <summary>A solid fill brush from a hex string.</summary>
    public static IBrush Fill(string hex) => new SolidColorBrush(Color.Parse(hex));

    /// <summary>
    /// The status/detail pill brushes for a semantic tone, mirroring the
    /// reference's <c>.pill-ok/.pill-warn/.pill-danger/.pill-info/.pill-mute</c>
    /// (styles.css) — bg from the phase-02 soft-bg token, text from the
    /// matching semantic token. «mute» uses the reference's literal
    /// <c>#eef2f0</c>/<c>#536b68</c> (no token exists for it).
    /// </summary>
    public static (IBrush Background, IBrush Foreground) PillBrushes(string tone) => tone switch
    {
        "ok" => (Fill("#ecfdf3"), Fill("#027a48")),
        "warn" => (Fill("#fffaeb"), Fill("#b54708")),
        "danger" => (Fill("#fef3f2"), Fill("#d92d20")),
        "info" => (Fill("#eff8ff"), Fill("#1570cd")),
        _ => (Fill("#eef2f0"), Fill("#536b68")), // mute
    };

    /// <summary>
    /// The stay-card border brush, mirroring <c>.bd-card--overdue</c> (danger)
    /// and <c>.bd-card--urgent</c> (warn) over the base <c>--line-2</c>.
    /// </summary>
    public static IBrush CardBorderBrush(bool overdue, bool urgent) =>
        overdue ? Fill("#d92d20") : urgent ? Fill("#b54708") : Fill("#eef2f5");

    /// <summary>
    /// The stay-card background, mirroring <c>.bd-card--overdue</c>
    /// (<c>#fff8f8</c>) and <c>.bd-card--urgent</c> (<c>#fffbf5</c>) over the
    /// base panel white.
    /// </summary>
    public static IBrush CardBackground(bool overdue, bool urgent) =>
        overdue ? Fill("#fff8f8") : urgent ? Fill("#fffbf5") : Fill("#ffffff");

    /// <summary>
    /// The stay-progress fill, mirroring <c>.bd-prog i</c> (brand-400→brand-600)
    /// for a normal stay and <c>.bd-card--urgent .bd-prog i</c> (warn→#d9980f)
    /// for an urgent/overdue one, and the danger fill for overdue
    /// (<c>.bd-prog--danger i</c>).
    /// </summary>
    public static IBrush ProgressFill(bool overdue, bool urgent)
    {
        var (from, to) = overdue
            ? ("#d92d20", "#c03030")
            : urgent
                ? ("#b54708", "#d9980f")
                : ("#2dd4bf", "#0d9488");

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse(from), 0),
                new GradientStop(Color.Parse(to), 1),
            },
        };
    }

    /// <summary>
    /// The full stay-progress bar brush, mirroring <c>.bd-prog</c> (bg-2 track)
    /// with <c>.bd-prog i</c> (brand/warn/danger fill) at
    /// <paramref name="percent"/>% — as one gradient so the bar is fully
    /// data-bound. The fill runs from the inline-start (right, RTL) edge, as the
    /// reference's block <c>i</c> does.
    /// </summary>
    public static IBrush ProgressBarBrush(bool overdue, bool urgent, int percent)
    {
        var (from, to) = overdue
            ? ("#d92d20", "#c03030")
            : urgent
                ? ("#b54708", "#d9980f")
                : ("#2dd4bf", "#0d9488");

        var frac = Math.Clamp(percent / 100d, 0d, 1d);
        var fillStart = Color.Parse(from);
        var fillEnd = Color.Parse(to);
        var track = Color.Parse("#f1f5f6");

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(fillStart, 0),
                new GradientStop(fillEnd, frac),
                new GradientStop(track, frac),
                new GradientStop(track, 1),
            },
        };
    }

    /// <summary>
    /// The day-label text colour, mirroring the reference's
    /// <c>bd-danger-txt / bd-urgent-txt / bd-warn-txt / bd-muted-txt</c> tone
    /// mapping in <c>renderCards</c>.
    /// </summary>
    public static IBrush DayLabelBrush(bool overdue, int remaining) =>
        overdue || remaining <= 0 ? Fill("#d92d20")
        : remaining <= 1 ? Fill("#b54708")
        : Fill("#667085");

    /// <summary>
    /// Money formatter mirroring the reference's
    /// <c>money = (n) => toFa(Number(n||0).toLocaleString('en-US')) + ' ریال'</c>
    /// (comma-grouped digits, Persian digits, trailing «ریال»).
    /// </summary>
    public static string Money(long value) =>
        PersianNumberFormatter.ToPersianDigits(value.ToString("N0", CultureInfo.InvariantCulture))
        + " ریال";

    /// <summary>Persian digits for an integer value (reference <c>fa(v)</c>).</summary>
    public static string Fa(int value) =>
        PersianNumberFormatter.ToPersianDigits(value.ToString(CultureInfo.InvariantCulture));
}
