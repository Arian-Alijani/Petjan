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
/// .oc-tag (.vip/.debt/.loyal).
/// </summary>
public sealed record OwnerTagViewModel(string Text, IBrush Background, IBrush Foreground);

/// <summary>
/// Adapts an <see cref="OwnerSummary"/> (plus its derived pets) for one owner
/// card / owner table row, pre-resolving every value the reference's
/// <c>ownerCard</c> / <c>ownersTable</c> render — avatar tone + initials, gender
/// pill, file badge, contact lines, tag pills, the pets sub-list with its
/// per-pet chips, and the footer (member-since + balance).
/// </summary>
public sealed class OwnerCardViewModel
{
    public OwnerCardViewModel(
        OwnerSummary owner,
        IReadOnlyList<PetSummary> allPets,
        IReadOnlyList<PetSummary> matchedPets)
    {
        Owner = owner;

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

        // The reference shows the pets that match the active filter, falling
        // back to the owner's full pet list when the filter excludes them all.
        var shown = matchedPets.Count > 0 ? matchedPets : allPets;
        Pets = shown.Select(pet => new OwnerPetChipViewModel(pet, owner)).ToArray();
        PetCountText = $"{PersianNumberFormatter.ToPersianDigits(allPets.Count)} حیوان ثبت‌شده";
        PetsPreviewText = allPets.Count == 0
            ? "—"
            : string.Join("، ", allPets.Take(2).Select(p => p.Name)) + (allPets.Count > 2 ? "…" : string.Empty);

        SinceText = $"مشتری از {JalaliDate.FromDateTime(owner.Since).ToString("short")}";
        BalanceText = owner.Balance > 0
            ? $"{PersianNumberFormatter.FormatGrouped(owner.Balance)} ت"
            : "۰";

        // Owners table: last visit across the owner's pets.
        var last = allPets
            .Where(p => p.LastVisitDate is not null)
            .OrderByDescending(p => p.LastVisitDate)
            .FirstOrDefault();
        LastVisitText = last?.LastVisitDate is { } date
            ? RelativeDateFormatter.Since(date)
            : "—";

        HasVaccines = allPets.Any(p => p.HasVaccines);
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
    public string BalanceText { get; }

    public string LastVisitText { get; }
    public bool HasVaccines { get; }
    public string VaccineStateText => HasVaccines ? "دارد" : "ندارد";
    public Controls.Tone VaccineStateTone => HasVaccines ? Controls.Tone.Ok : Controls.Tone.Neutral;

    private static string FormatPhone(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "ثبت نشده" : PersianNumberFormatter.ToPersianDigits(value);
}

/// <summary>One virtualized directory row containing at most three cards.</summary>
public sealed record OwnerCardRowViewModel(IReadOnlyList<OwnerCardViewModel> Cards);
