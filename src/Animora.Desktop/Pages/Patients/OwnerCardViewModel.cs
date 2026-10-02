using Animora.Contracts.Patients;
using Animora.Desktop.Localization;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Patients;

/// <summary>
/// One owner's contact line (icon + value, with a muted placeholder when the
/// value is missing), mirroring the reference's <c>ownerCard</c> .oc-line rows.
/// </summary>
public sealed record OwnerContactLineViewModel(string IconKey, string Value, bool IsMuted);

/// <summary>
/// One owner-card tag pill (name + resolved palette), mirroring the reference's
/// .oc-tag (.vip/.loyal).
/// </summary>
public sealed record OwnerTagViewModel(string Text, IBrush Background, IBrush Foreground);

/// <summary>
/// Adapts an <see cref="OwnerSummary"/> (plus its derived pets) for one owner
/// card / owner table row, pre-resolving every value the reference's
/// <c>ownerCard</c> / <c>ownersTable</c> render — avatar tone + initials, gender
/// pill, file badge, contact lines, tag pills, the pets sub-list with its
/// per-pet chips, and the footer (member-since).
/// </summary>
public sealed class OwnerCardViewModel
{
    /// <summary>
    /// Builds the card from an <see cref="OwnerSummary"/>. The owner's pet roster is optional: with
    /// server-side owner paging the page no longer holds every pet in RAM, so <paramref name="pets"/>
    /// may be empty — the pet count then comes from <see cref="OwnerSummary.PetCount"/> (projected in
    /// SQL) and the pet chips/preview degrade gracefully instead of forcing an N+1 roster fetch.
    /// </summary>
    public OwnerCardViewModel(OwnerSummary owner, IReadOnlyList<PetSummary>? pets = null)
    {
        Owner = owner;
        var roster = pets ?? [];

        Name = $"{owner.FirstName} {owner.LastName}";
        Initials = PatientsPresentation.Initials(owner.FirstName, owner.LastName);
        GenderLabel = PatientsPresentation.OwnerGenderLabel(owner.Gender);
        FileText = $"پرونده {owner.File}";
        ToneBrush = PatientsPresentation.ToneBrush(owner.Tone);

        ContactLines =
        [
            new OwnerContactLineViewModel("IconMobile", FormatPhone(owner.Mobile1), false),
            new OwnerContactLineViewModel("IconMobile", FormatPhone(owner.Mobile2), string.IsNullOrEmpty(owner.Mobile2)),
            new OwnerContactLineViewModel("IconPhone", FormatPhone(owner.Phone), string.IsNullOrEmpty(owner.Phone)),
            new OwnerContactLineViewModel("IconIdCard", PersianNumberFormatter.ToPersianDigits(owner.NationalId), false),
        ];
        Address = string.IsNullOrWhiteSpace(owner.Address) ? "—" : owner.Address;

        Tags = owner.Tags
            .Select(tag =>
            {
                var (bg, fg) = PatientsPresentation.TagTone(tag);
                return new OwnerTagViewModel(tag, bg, fg);
            })
            .ToArray();

        // Pet count is the SQL-projected OwnerSummary.PetCount (always accurate), not the roster size.
        var petCount = owner.PetCount > 0 ? owner.PetCount : roster.Count;
        Pets = roster.Select(pet => new OwnerPetChipViewModel(pet, owner)).ToArray();
        PetCountText = $"{PersianNumberFormatter.ToPersianDigits(petCount)} حیوان ثبت‌شده";
        PetsPreviewText = roster.Count == 0
            ? "—"
            : string.Join("، ", roster.Take(2).Select(p => p.Name)) + (roster.Count > 2 ? "…" : string.Empty);

        SinceText = $"مشتری از {JalaliDate.FromDateTime(owner.Since).ToString("short")}";

        // Owners table: last visit across the owner's pets (none in C01 — no visit source yet).
        var last = roster
            .Where(p => p.LastVisitDate is not null)
            .OrderByDescending(p => p.LastVisitDate)
            .FirstOrDefault();
        LastVisitText = last?.LastVisitDate is { } date
            ? RelativeDateFormatter.Since(date)
            : "—";

        HasVaccines = roster.Any(p => p.HasVaccines);
    }

    public OwnerSummary Owner { get; }

    public string Name { get; }
    public string Initials { get; }
    public string GenderLabel { get; }
    public string FileText { get; }
    public IBrush ToneBrush { get; }

    public IReadOnlyList<OwnerContactLineViewModel> ContactLines { get; }
    public string Address { get; }
    public IReadOnlyList<OwnerTagViewModel> Tags { get; }
    public IReadOnlyList<OwnerPetChipViewModel> Pets { get; }
    public string PetCountText { get; }

    /// <summary>
    /// Compact two-name preview of the owner's full pet roster for the owners
    /// table (independent of the active species filter). Assigned once in the
    /// constructor from <c>allPets</c>.
    /// </summary>
    public string PetsPreviewText { get; }

    public string SinceText { get; }

    public string LastVisitText { get; }
    public bool HasVaccines { get; }
    public string VaccineStateText => HasVaccines ? "دارد" : "ندارد";
    public Controls.Tone VaccineStateTone => HasVaccines ? Controls.Tone.Ok : Controls.Tone.Neutral;

    private static string FormatPhone(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "ثبت نشده" : PersianNumberFormatter.ToPersianDigits(value);
}

/// <summary>One virtualized directory row containing at most three cards.</summary>
public sealed record OwnerCardRowViewModel(IReadOnlyList<OwnerCardViewModel> Cards);
