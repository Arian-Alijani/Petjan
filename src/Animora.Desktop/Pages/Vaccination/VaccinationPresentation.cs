using System.Globalization;
using Animora.Contracts.Vaccination;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Avalonia;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Vaccination;

/// <summary>
/// Shared presentation mapping for the Vaccination screen. Every value here is
/// transcribed from the screen's own CSS/JS in the reference
/// (Animora-UI/vaccination.css + assets/vaccination.js +
/// assets/vaccination-data.js) — the pet-species glyph keys, the vaccine accent
/// palette, the due-date urgency classes, the SMS-state tones and the
/// catalog-card tone colors — so the rows, cards and the compose drawer can
/// bind pre-resolved brushes/keys without any UI logic leaking into the data
/// contracts.
/// </summary>
public static class VaccinationPresentation
{
    /// <summary>
    /// Species glyph key, mirroring the reference's <c>VAX_SPECIES[k].icon</c>
    /// (falls back to the paw glyph, matching <c>vaxById</c>/<c>speciesLabel</c>'s
    /// «سایر» default).
    /// </summary>
    public static string SpeciesIconKey(string? species) => species switch
    {
        "cat" => "IconCat",
        "dog" => "IconDog",
        "bird" => "IconBird",
        "rabbit" => "IconRabbit",
        "paw" => "IconPaw",
        _ => "IconPaw",
    };

    /// <summary>
    /// Species display label, mirroring <c>VAX_SPECIES[k].label</c> (default
    /// «سایر»).
    /// </summary>
    public static string SpeciesLabel(string? species) => species switch
    {
        "cat" => "گربه",
        "dog" => "سگ",
        "bird" => "پرنده",
        "rabbit" => "خرگوش",
        "all" => "همه گونه‌ها",
        _ => "سایر",
    };

    /// <summary>
    /// Status pill text + tone, mirroring the reference's <c>vaxStatusMeta</c>
    /// (due→nزدیک/warn, overdue→عقب‌افتاده/danger, scheduled→برنامه‌ریزی‌شده/info,
    /// done→انجام‌شده/ok).
    /// </summary>
    public static (string Label, Tone Tone) Status(VaccinationStatus status) => status switch
    {
        VaccinationStatus.Due => ("نزدیک", Tone.Warn),
        VaccinationStatus.Overdue => ("عقب‌افتاده", Tone.Danger),
        VaccinationStatus.Scheduled => ("برنامه‌ریزی‌شده", Tone.Info),
        VaccinationStatus.Done => ("انجام‌شده", Tone.Ok),
        _ => ("—", Tone.Neutral),
    };

    /// <summary>
    /// SMS-cell text/icon/tone, mirroring the reference's <c>vaxSmsMeta</c>
    /// (none→ارسال‌نشده/mute, sent→ارسال‌شده/ok, scheduled→زمان‌بندی‌شده/info,
    /// failed→ناموفق/danger).
    /// </summary>
    public static (string Label, string IconKey, Tone Tone) Sms(SmsState state) => state switch
    {
        SmsState.None => ("ارسال‌نشده", "IconSms", Tone.Neutral),
        SmsState.Sent => ("ارسال‌شده", "IconCheck", Tone.Ok),
        SmsState.Scheduled => ("زمان‌بندی‌شده", "IconClock", Tone.Info),
        SmsState.Failed => ("ناموفق", "IconAlert", Tone.Danger),
    };

    /// <summary>
    /// The vaccine-catalog tone palette, mirroring the reference's inline
    /// <c>toneColor</c> map in <c>renderCatalog</c>.
    /// </summary>
    public static string CatalogToneColor(string? tone) => tone switch
    {
        "a" => "#8f74d1",
        "b" => "#e08a5c",
        "c" => "#eab749",
        "d" => "#d16060",
        "e" => "#4aa591",
        "f" => "#5c8ad6",
        _ => "#8f74d1",
    };

    /// <summary>
    /// The pet avatar gradient fill, mirroring
    /// <c>.avatar-pet.(a..f)</c> in styles.css.
    /// </summary>
    public static IBrush AvatarFill(string? tone) => tone switch
    {
        "a" => Vertical("#b8a2e6", "#8f74d1"),
        "b" => Vertical("#f0b593", "#e08a5c"),
        "c" => Vertical("#fcd97a", "#eab749"),
        "d" => Vertical("#ea8f8f", "#d16060"),
        "e" => Vertical("#7ac7b6", "#4aa591"),
        "f" => Vertical("#8fb5f0", "#5c8ad6"),
        _ => Vertical("#f1f5f6", "#e5eaee"),
    };

    /// <summary>The first character of a pet name, standing in for its avatar glyph.</summary>
    public static string Initial(string name) => string.IsNullOrWhiteSpace(name) ? "؟" : name[..1];

    /// <summary>
    /// The relative-due label + urgency class, mirroring the reference's
    /// <c>dueCell</c>: done→«نوبت بعدی» (far), past→«N روز گذشته» (past),
    /// today→«امروز» (soon), ≤14→«N روز مانده» (soon), else→«N روز مانده» (far).
    /// </summary>
    public static (string Text, string Urgency) RelativeDue(VaccinationRecord record)
    {
        if (record.Status == VaccinationStatus.Done)
        {
            return ("نوبت بعدی", "far");
        }

        var days = record.DueDate.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber;
        if (days < 0)
        {
            return ($"{Fa(Math.Abs(days))} روز گذشته", "past");
        }

        if (days == 0)
        {
            return ("امروز", "soon");
        }

        var cls = days <= 14 ? "soon" : "far";
        return ($"{Fa(days)} روز مانده", cls);
    }

    /// <summary>Jalali long date string, mirroring <c>VJ.long()</c>.</summary>
    public static string LongDate(DateOnly date) =>
        JalaliDate.FromDateTime(date.ToDateTime(TimeOnly.MinValue)).ToString("long");

    /// <summary>Jalali long date or an em dash when null (matches <c>r.lastDate === '—'</c>).</summary>
    public static string LongDateOrDash(DateOnly? date) =>
        date is { } d ? LongDate(d) : "—";

    /// <summary>Persian digits for a plain value.</summary>
    public static string Fa(int value) =>
        PersianNumberFormatter.ToPersianDigits(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Persian digits for an arbitrary string (mobile, lot, …).</summary>
    public static string FaString(string? value) => PersianNumberFormatter.ToPersianDigits(value ?? string.Empty);

    /// <summary>Top-to-bottom two-stop gradient.</summary>
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

    /// <summary>A solid fill brush from a hex string.</summary>
    public static IBrush Fill(string hex) => new SolidColorBrush(Color.Parse(hex));

    /// <summary>A fill brush with an alpha channel over a hex color (reference's <c>col + '22'</c>).</summary>
    public static IBrush FillAlpha(string hex, byte alpha)
    {
        var color = Color.Parse(hex);
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }
}
