using System.Globalization;
using Animora.Contracts.Boarding;
using Animora.Desktop.Controls;
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

        // Shared via GradientBrushCache — the six tone pairs are a fixed set,
        // so this never allocates a brush per stay card (PERFORMANCE.md §10).
        return GradientBrushCache.LinearGradient(from, to, 0, 0, 1, 1);
    }

    /// <summary>
    /// The stay-card avatar glyph colour, mirroring the reference's per-tone
    /// <c>color</c> (the amber «c» tone draws dark ink, all others white).
    /// </summary>
    public static IBrush AvatarGlyphBrush(string? tone) =>
        tone == "c" ? Fill("#101828") : Fill("#ffffff");

    /// <summary>A solid fill brush from a hex string (shared via <see cref="BrushCache"/>).</summary>
    public static IBrush Fill(string hex) => BrushCache.Solid(hex);

    /// <summary>
    /// The status/detail pill brushes for a semantic tone, mirroring the
    /// reference's <c>.pill-ok/.pill-warn/.pill-danger/.pill-info/.pill-mute</c>
    /// (styles.css) — bg from the phase-02 soft-bg token, text from the
    /// matching semantic token. «mute» uses the reference's literal
    /// <c>#eef1f8</c>/<c>#536b68</c> (no token exists for it).
    /// </summary>
    public static (IBrush Background, IBrush Foreground) PillBrushes(string tone) => tone switch
    {
        "ok" => (Fill("#ecfdf3"), Fill("#027a48")),
        "warn" => (Fill("#fffaeb"), Fill("#b54708")),
        "danger" => (Fill("#fef3f2"), Fill("#d92d20")),
        "info" => (Fill("#eff8ff"), Fill("#1570cd")),
        _ => (Fill("#eef1f8"), Fill("#536b68")), // mute
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
                : ("#8aa2fb", "#4a6cf7");

        // Shared via GradientBrushCache — three fixed tone pairs (PERFORMANCE.md §10).
        return GradientBrushCache.LinearGradient(from, to, 0, 0, 1, 0);
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
                : ("#8aa2fb", "#4a6cf7");

        // Shared via GradientBrushCache. The three tone pairs and the single
        // track colour are fixed; the only continuous input, `percent`, is
        // quantised to a whole percent (0..100) inside the cache so the key
        // space stays bounded to 101 values per tone (PERFORMANCE.md §10/§15).
        return GradientBrushCache.ProgressBar(from, to, "#f1f5f6", percent);
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

    /// <summary>Reference <c>J().long(date)</c> — «۱۴ مهر ۱۴۰۴», Persian digits.</summary>
    public static string Long(DateOnly date) =>
        JalaliDate.FromDateTime(date.ToDateTime(TimeOnly.MinValue)).ToString("long");

    /// <summary>Reference <c>J().medium(date)</c> — «۱۴ مهر», Persian digits.</summary>
    public static string Medium(DateOnly date) =>
        JalaliDate.FromDateTime(date.ToDateTime(TimeOnly.MinValue)).ToString("medium");

    /// <summary>Reference <c>J().short(date)</c> — «۱۴۰۴/۰۷/۱۴», Persian digits.</summary>
    public static string Short(DateOnly date) =>
        JalaliDate.FromDateTime(date.ToDateTime(TimeOnly.MinValue)).ToString("short");

    /// <summary>
    /// Parses a Jalali «YYYY/MM/DD» text field (Persian or ASCII digits, as the
    /// reference's <c>#bd-f-in</c>/<c>#bd-f-out</c> inputs hold) back to a
    /// <see cref="DateOnly"/>. Returns null when the shape or values are invalid
    /// — mirroring the reference's <c>/^\d{4}\/\d{2}\/\d{2}$/</c> guard plus a
    /// real calendar-validity check.
    /// </summary>
    public static DateOnly? ParseJalali(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Normalize Persian digits + Arabic yeh/kaf to their ASCII form.
        var ascii = PersianText.Normalize(text);
        var parts = ascii.Split('/');
        if (parts.Length != 3)
        {
            return null;
        }

        if (!int.TryParse(parts[0], out var year)
            || !int.TryParse(parts[1], out var month)
            || !int.TryParse(parts[2], out var day))
        {
            return null;
        }

        if (year is < 1300 or > 1500 || month is < 1 or > 12 || day < 1 || day > 31)
        {
            return null;
        }

        try
        {
            return DateOnly.FromDateTime(new JalaliDate(year, month, day).ToDateTime());
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Reference <c>payLabel(mode)</c> — «پیش‌پرداخت» / «پس‌پرداخت».</summary>
    public static string PayLabel(BoardingPaymentMode mode) =>
        mode == BoardingPaymentMode.Prepaid ? "پیش‌پرداخت" : "پس‌پرداخت";

    /// <summary>Reference <c>payTone(mode)</c> — prepaid ok, postpaid info.</summary>
    public static string PayTone(BoardingPaymentMode mode) =>
        mode == BoardingPaymentMode.Prepaid ? "ok" : "info";

    /// <summary>Reference <c>ROOMS[].type</c> chip text — «VIP» / «معمولی».</summary>
    public static string RoomTypeLabel(BoardingRoomType type) =>
        type == BoardingRoomType.Vip ? "VIP" : "معمولی";

    /// <summary>The room-kind chip text of the settings tab («معمولی» / «VIP» / «سوئیت» / «ایزوله» / «قفس»).</summary>
    public static string RoomKindLabel(BoardingRoomKind kind) => kind switch
    {
        BoardingRoomKind.Vip => "VIP",
        BoardingRoomKind.Suite => "سوئیت",
        BoardingRoomKind.Isolation => "ایزوله",
        BoardingRoomKind.Cage => "قفس",
        _ => "معمولی",
    };

    /// <summary>Reference consumed-row tag — «خدمت» / «کالا».</summary>
    public static string ItemKindLabel(BoardingItemKind kind) =>
        kind == BoardingItemKind.Item ? "کالا" : "خدمت";

    /// <summary>
    /// The consumed-row tag brush, mirroring <c>.bd-cons-tag</c> (brand-50 /
    /// brand-700) and <c>.bd-cons-tag.item</c> (lilac-bg / #7b5cc4).
    /// </summary>
    public static (IBrush Background, IBrush Foreground) ItemTagBrushes(BoardingItemKind kind) =>
        kind == BoardingItemKind.Item
            ? (Fill("#efe7fb"), Fill("#7b5cc4"))
            : (Fill("#eef2fe"), Fill("#3a56d4"));
}
