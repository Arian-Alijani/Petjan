using Animora.Domain.Events;
using Animora.Domain.Primitives;
using Animora.Domain.Time;

namespace Animora.Domain.Patients;

/// <summary>Species groups the clinic treats.</summary>
public enum PetSpecies
{
    /// <summary>Dog.</summary>
    Dog = 0,

    /// <summary>Cat.</summary>
    Cat = 1,

    /// <summary>Bird.</summary>
    Bird = 2,

    /// <summary>Rodent / small mammal.</summary>
    Rodent = 3,

    /// <summary>Reptile.</summary>
    Reptile = 4,

    /// <summary>Anything else; the breed field carries the detail.</summary>
    Other = 5,

    /// <summary>Rabbit (appended after <see cref="Other"/> so persisted integer values never shift).</summary>
    Rabbit = 6,
}

/// <summary>Gender recorded for a patient.</summary>
public enum PetGender
{
    /// <summary>Not determined yet.</summary>
    Unknown = 0,

    /// <summary>Male.</summary>
    Male = 1,

    /// <summary>Female.</summary>
    Female = 2,
}

/// <summary>Lifecycle state of a patient record.</summary>
public enum PetStatus
{
    /// <summary>Currently a patient of the clinic.</summary>
    Active = 0,

    /// <summary>No longer seen (moved away, transferred); the history stays readable.</summary>
    Inactive = 1,

    /// <summary>Deceased; the record is closed but never removed.</summary>
    Deceased = 2,
}

/// <summary>
/// The patient. Owned by exactly one <see cref="Owner"/> and the anchor of every clinical record.
/// </summary>
/// <remarks>
/// <see cref="Species"/> is <b>immutable after creation</b>: a species change is a data-entry
/// correction that must produce a new record, because the whole clinical history (vaccine schedules,
/// dosages, reference ranges) is interpreted through it. There is deliberately no mutator, and
/// <c>ErrorCodes.PetSpeciesImmutable</c> is what the Application layer returns when a caller asks.
/// </remarks>
public sealed class Pet : AggregateRoot
{
    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private Pet()
    {
    }

    /// <summary>The <see cref="Owner"/> this patient belongs to.</summary>
    public string OwnerId { get; private set; } = string.Empty;

    /// <summary>Call name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Species — set once at registration and never changed.</summary>
    public PetSpecies Species { get; private set; }

    /// <summary>Breed, free text (breed lists differ per species and per region).</summary>
    public string? Breed { get; private set; }

    /// <summary>Recorded gender.</summary>
    public PetGender Gender { get; private set; } = PetGender.Unknown;

    /// <summary>Date of birth, UTC; <see langword="null"/> when unknown.</summary>
    public DateTimeOffset? BirthDateUtc { get; private set; }

    /// <summary><see langword="true"/> when the patient is neutered/spayed.</summary>
    public bool IsNeutered { get; private set; }

    /// <summary>Coat/plumage colour.</summary>
    public string? Color { get; private set; }

    /// <summary>Microchip code, when implanted.</summary>
    public string? MicrochipCode { get; private set; }

    /// <summary>When the microchip was implanted, UTC.</summary>
    public DateTimeOffset? MicrochipImplantUtc { get; private set; }

    /// <summary>Lifecycle state.</summary>
    public PetStatus Status { get; private set; } = PetStatus.Active;

    /// <summary>Known allergies — surfaced as a banner on every clinical screen.</summary>
    public string? AllergyNotes { get; private set; }

    /// <summary>Chronic conditions — surfaced alongside <see cref="AllergyNotes"/>.</summary>
    public string? ChronicNotes { get; private set; }

    /// <summary>When the patient was registered, UTC.</summary>
    public DateTimeOffset RegisteredUtc { get; private set; }

    /// <summary>Persisted, normalized search index maintained by the persistence layer's ISearchKeyProvider
    /// on every write (C01). Derived data — the domain never reads it and a merge never writes it.</summary>
    public string SearchKey { get; set; } = string.Empty;

    /// <summary>
    /// Registers an active patient and raises <see cref="PetRegisteredEvent"/> so A10 can append the
    /// opening medical-history entry.
    /// </summary>
    public static Pet Register(
        string tenantId,
        string ownerId,
        string name,
        PetSpecies species,
        IClock clock,
        PetGender gender = PetGender.Unknown,
        DateTimeOffset? birthDateUtc = null,
        string? breed = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.UtcNow;
        var pet = new Pet
        {
            Id = IdGenerator.NewId(now),
            TenantId = tenantId,
            OwnerId = ownerId,
            Name = name.Trim(),
            Species = species,
            Gender = gender,
            BirthDateUtc = birthDateUtc,
            Breed = string.IsNullOrWhiteSpace(breed) ? null : breed.Trim(),
            Status = PetStatus.Active,
            RegisteredUtc = now,
        };

        pet.Raise(new PetRegisteredEvent(IdGenerator.NewId(now), tenantId, now, pet.Id, ownerId));
        return pet;
    }

    /// <summary>
    /// Age in whole months at <paramref name="nowUtc"/>; <see langword="null"/> when the birth date is
    /// unknown, and <c>0</c> for a patient born in the future (a data-entry slip must not read negative).
    /// Computed, never stored — a stored age drifts.
    /// </summary>
    public int? AgeMonthsAt(DateTimeOffset nowUtc)
    {
        if (BirthDateUtc is not { } birth)
        {
            return null;
        }

        var months = ((nowUtc.Year - birth.Year) * 12) + nowUtc.Month - birth.Month;
        if (nowUtc.Day < birth.Day)
        {
            months--;
        }

        return months < 0 ? 0 : months;
    }

    /// <summary>Updates the descriptive block. <see cref="Species"/> is deliberately absent.</summary>
    public void UpdateProfile(
        string name,
        string? breed = null,
        PetGender? gender = null,
        DateTimeOffset? birthDateUtc = null,
        bool? isNeutered = null,
        string? color = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        Breed = Clean(breed);
        Gender = gender ?? Gender;
        BirthDateUtc = birthDateUtc ?? BirthDateUtc;
        IsNeutered = isNeutered ?? IsNeutered;
        Color = Clean(color);
    }

    /// <summary>Records or replaces the microchip identification.</summary>
    public void SetMicrochip(string code, DateTimeOffset implantedUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        MicrochipCode = code.Trim();
        MicrochipImplantUtc = implantedUtc;
    }

    /// <summary>Replaces the allergy / chronic-condition banners.</summary>
    public void SetClinicalFlags(string? allergyNotes, string? chronicNotes)
    {
        AllergyNotes = Clean(allergyNotes);
        ChronicNotes = Clean(chronicNotes);
    }

    /// <summary>Moves the record between <see cref="PetStatus"/> values; history is never removed.</summary>
    public void SetStatus(PetStatus status) => Status = status;

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
