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
        PetSpecies.Reptile => "خزندگان",
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
            PetSpecies.Reptile => ("#e6f3d9", "#f6fbf0"),
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
        PetStatus.Inactive => "غیرفعال",
        PetStatus.Deceased => "فوت‌شده",
        _ => "سالم",
    };

    public static Tone StatusTone(PetStatus status) => status switch
    {
        PetStatus.FollowUp => Tone.Warn,
        PetStatus.Critical => Tone.Danger,
        PetStatus.Inactive => Tone.Neutral,
        PetStatus.Deceased => Tone.Neutral,
        _ => Tone.Ok,
    };

    // ---- sex dot (patients-ui.js sexDot + patients.css .sex-dot) ----

    public static SexDotInfo SexDot(PetGender gender) => gender switch
    {
        PetGender.Male => new SexDotInfo("♂", "نر", Brush("#eff8ff"), Brush("#1570cd")),
        PetGender.Female => new SexDotInfo("♀", "ماده", Brush("#fbe4f1"), Brush("#a8478a")),
        _ => new SexDotInfo("؟", "نامشخص", Brush("#eef2f0"), Brush("#7a8b89")),
    };

    // ---- owner gender / tags (patients-data.js + patients.css) ----

    public static string OwnerGenderLabel(OwnerGender gender) =>
        gender == OwnerGender.Male ? "آقا" : "خانم";

    /// <summary>Tag pill palette (patients.css .oc-tag.vip/.loyal).</summary>
    public static (IBrush Background, IBrush Foreground) TagTone(string tag) => tag switch
    {
        "VIP" => (Brush("#f4f3ff"), Brush("#7b5cc4")),
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

    // ---- pet-file hero tone (patients-page.js HERO_TONE) ----
    // NOTE: distinct copy from the list STATUS_MAP above — the pet file's hero
    // chip reads «وضعیت پایدار / در حال پیگیری / بیمار مزمن».

    public static string HeroStatusLabel(PetStatus status) => status switch
    {
        PetStatus.FollowUp => "در حال پیگیری",
        PetStatus.Critical => "بیمار مزمن",
        PetStatus.Inactive => "غیرفعال",
        PetStatus.Deceased => "فوت‌شده",
        _ => "وضعیت پایدار",
    };

    /// <summary>Hero chip tone class (ok/warn/alert/muted).</summary>
    public static string HeroStatusClass(PetStatus status) => status switch
    {
        PetStatus.FollowUp => "warn",
        PetStatus.Critical => "alert",
        PetStatus.Inactive => "muted",
        PetStatus.Deceased => "muted",
        _ => "ok",
    };

    // ---- lab status (patients-page.js petDocsTab map) ----

    public static Tone LabTone(LabStatus status) => status switch
    {
        LabStatus.Warn => Tone.Warn,
        LabStatus.Danger => Tone.Danger,
        LabStatus.Pending => Tone.Info,
        _ => Tone.Ok,
    };

    /// <summary>Timeline dot tone for a visit type (patients-page.js petVisitsTab dotTone).</summary>
    public static string VisitDotVax(string type) => type switch
    {
        "واکسیناسیون" => "violet",
        "جراحی" => "danger",
        "درمانی" => "amber",
        "پیگیری" => "info",
        _ => string.Empty,
    };

    /// <summary>Species breeds (patients-data.js SPECIES[].breeds).</summary>
    public static IReadOnlyList<string> Breeds(PetSpecies species) => species switch
    {
        PetSpecies.Cat => ["پرشین", "اسکاتیش فولد", "برمه‌ای", "مو کوتاه اهلی", "رگدال", "بنگال"],
        PetSpecies.Dog => ["گلدن رتریور", "شیتزو", "پامرانین", "هاسکی", "ژرمن شپرد", "پودل"],
        PetSpecies.Bird => ["طوطی کاسکو", "کاکادو", "مرغ عشق", "قناری", "روزلا"],
        PetSpecies.Rabbit => ["لوپ هلندی", "آنقوره", "رکس", "مینی لوپ"],
        PetSpecies.Rodent => ["همستر سوری", "خوکچه هندی", "چینچیلا"],
        PetSpecies.Reptile => ["لاک‌پشت", "ایگوانا", "مار"],
        _ => ["سایر"],
    };

    /// <summary>Reference taxonomy option lists (patients-data.js).</summary>
    public static IReadOnlyList<string> Referrals { get; } =
        ["معرفی دوستان", "اینستاگرام", "جستجوی گوگل", "تابلوی کلینیک", "پت‌شاپ همکار", "دامپزشک ارجاع‌دهنده", "سایر"];

    public static IReadOnlyList<string> Habitats { get; } =
        ["آپارتمان", "خانه حیاط‌دار", "باغ / فضای باز", "قفس", "آکواریوم / تراریوم", "خیابانی (سرپرستی)"];

    public static IReadOnlyList<string> Behaviors { get; } =
        ["آرام و اجتماعی", "بازیگوش", "مضطرب", "پرخاشگر با غریبه", "ترسو", "کنجکاو"];

    public static IReadOnlyList<string> Diets { get; } =
        ["غذای خشک تجاری", "غذای کنسروی", "غذای خانگی", "رژیم درمانی (رنال)", "رژیم درمانی (گوارشی)", "مخلوط خشک و تر", "دانه و میوه"];

    /// <summary>Species display order for the wizard's type tiles (patients-data.js SPECIES order).</summary>
    public static IReadOnlyList<PetSpecies> SpeciesOrder { get; } =
        [PetSpecies.Cat, PetSpecies.Dog, PetSpecies.Bird, PetSpecies.Rabbit, PetSpecies.Rodent, PetSpecies.Reptile, PetSpecies.Other];

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

    // ---- shared command-failure messaging ----
    //
    // The Patients data service surfaces every backend failure as an
    // InvalidOperationException whose Message is the backend error code. The
    // create/save flows (wizard, add-pet modal, owner sheet) must catch these and
    // show a toast — never let them bubble out of an async command, where the
    // global handler would otherwise treat them as a process crash
    // ("CRASH_RECOVERY_REQUIRED").

    /// <summary>Shown when a permission / session / capability failure is detected.</summary>
    public const string AccessLostMessage =
        "دسترسی لازم را ندارید یا نشست منقضی شده؛ دوباره وارد شوید";

    /// <summary>True for permission / session / capability failures (as opposed to a store or code fault).</summary>
    public static bool IsAccessFailure(string code) =>
        code is "PERMISSION_DENIED" or "PERMISSION_UNKNOWN" or "SESSION_EXPIRED"
            or "CAPABILITY_NOT_GRANTED" or "CAPABILITY_SNAPSHOT_MISSING" or "CAPABILITY_SNAPSHOT_TAMPERED"
            or "MEMBERSHIP_NOT_ACTIVE" or "USER_DISABLED" or "USER_LOCKED_OUT" or "TENANT_MISMATCH"
            or "TENANT_SUSPENDED" or "DEVICE_REVOKED";

    /// <summary>
    /// Maps an <see cref="InvalidOperationException"/> raised by a Patients create/save command to the
    /// user-facing message to toast: the access-lost copy for permission/session failures, otherwise the
    /// supplied fallback (a plain "operation failed; try again" line for store/code faults).
    /// </summary>
    public static string CommandFailureMessage(System.InvalidOperationException ex, string fallback) =>
        IsAccessFailure(ex.Message) ? AccessLostMessage : fallback;

    // Delegates to the shared, process-wide caches so callers that hit the hot
    // path (card/row builds) never re-allocate a brush per item
    // (PERFORMANCE.md §10).
    private static IBrush Brush(string hex) => BrushCache.Solid(hex);

    private static IBrush Diagonal(string from, string to) =>
        GradientBrushCache.LinearGradient(from, to, 0, 0, 1, 1);
}
