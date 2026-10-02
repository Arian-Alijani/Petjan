using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Text;
using Animora.Application.UseCases.Patients.ReadModels;
using Animora.Domain.Patients;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>C01 projection reads for the Patients directory + detail screens. Tenant/soft-delete filters are
/// applied automatically by ModelConventions; each list is one SELECT + one COUNT via QueryPaging.</summary>
public sealed class PatientDirectoryQueries(AnimoraLocalDbContext db, IOptions<LocalStoreOptions> options)
    : IPatientDirectoryQueries
{
    private LocalStoreOptions Options => options.Value;

    public async Task<PagedResult<PetSummaryReadModel>> GetPetsAsync(
        string? search, PetSpecies? species, PatientDirectorySort sort,
        int page, int pageSize, CancellationToken ct)
    {
        var filtered = FilterPets(db.Set<Pet>().AsNoTracking(), search, species);

        // Stable ORDER BY with a unique id tiebreaker so Skip/Take never drifts. No visit source
        // exists yet in C01, so "Recent" falls back to name-ordered (same as Name) until C03.
        var ordered = sort switch
        {
            PatientDirectorySort.Age => filtered.OrderBy(p => p.BirthDateUtc).ThenBy(p => p.Id),
            _ => filtered.OrderBy(p => p.Name).ThenBy(p => p.Id),
        };

        var projected = ordered
            .Select(p => new PetSummaryReadModel(
                p.Id, p.Name, p.Species, p.Breed, p.Color, p.Gender, p.Status, p.OwnerId, p.BirthDateUtc,
                db.Set<WeightEntry>().Where(w => w.PetId == p.Id)
                    .OrderByDescending(w => w.MeasuredUtc).Select(w => (decimal?)w.WeightKg).FirstOrDefault(),
                null, null,   // LastVisitUtc / LastVisitType arrive with C03; no visit source yet.
                p.MicrochipCode));
        return await projected.ToPagedAsync(page, pageSize, Options, ct);
    }

    public async Task<PagedResult<OwnerSummaryReadModel>> GetOwnersAsync(
        string? search, PetSpecies? species, PatientDirectorySort sort,
        int page, int pageSize, CancellationToken ct)
    {
        var owners = FilterOwners(search, species);

        var ordered = sort switch
        {
            PatientDirectorySort.Name => owners.OrderBy(o => o.FullName).ThenBy(o => o.Id),
            PatientDirectorySort.Age => owners.OrderBy(o => o.RegisteredUtc).ThenBy(o => o.Id),
            _ => owners.OrderByDescending(o => o.RegisteredUtc).ThenBy(o => o.Id),
        };

        var projected = ordered
            .Select(o => new OwnerSummaryReadModel(
                o.Id, o.FullName, o.PhonePrimary, o.PhoneSecondary, o.NationalId, o.Address, o.Gender,
                o.RegisteredUtc,
                db.Set<Pet>().Count(p => p.OwnerId == o.Id)));
        return await projected.ToPagedAsync(page, pageSize, Options, ct);
    }

    public async Task<PatientDirectoryFacetsReadModel> GetFacetsAsync(
        string? search, PetSpecies? species, CancellationToken ct)
    {
        // Totals under the active search/species filter (owners cross-filtered to those
        // with a matching pet, exactly like GetOwnersAsync).
        var totalPets = await FilterPets(db.Set<Pet>().AsNoTracking(), search, species).CountAsync(ct);
        var totalOwners = await FilterOwners(search, species).CountAsync(ct);

        // Per-species chip counts ignore the species filter (chips show the full taxonomy under
        // the current text filter) — one grouped COUNT, not N queries.
        var speciesCounts = await FilterPets(db.Set<Pet>().AsNoTracking(), search, species: null)
            .GroupBy(p => p.Species)
            .Select(g => new { Species = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Species, x => x.Count, ct);

        return new PatientDirectoryFacetsReadModel(totalOwners, totalPets, speciesCounts);
    }

    /// <summary>Shared pet filter (search + species) reused by the list page and the
    /// facet/owner cross-filter so every count and every page agree on one predicate.</summary>
    private static IQueryable<Pet> FilterPets(
        IQueryable<Pet> q, string? search, PetSpecies? species)
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = PersianText.Normalize(search);
            q = q.Where(p => EF.Functions.Like(p.SearchKey, $"%{term}%"));
        }
        if (species is { } sp) q = q.Where(p => p.Species == sp);
        return q;
    }

    /// <summary>Owner filter: text match on the owner's SearchKey, plus — when a species
    /// filter is active — an EXISTS narrowing to owners with at least one matching pet (the
    /// owner⇄pet cross-filter runs in SQL, never client-side).</summary>
    private IQueryable<Owner> FilterOwners(string? search, PetSpecies? species)
    {
        var owners = db.Set<Owner>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = PersianText.Normalize(search);
            owners = owners.Where(o => EF.Functions.Like(o.SearchKey, $"%{term}%"));
        }
        if (species is not null)
        {
            var matchingPets = FilterPets(db.Set<Pet>().AsNoTracking(), search: null, species);
            owners = owners.Where(o => matchingPets.Any(p => p.OwnerId == o.Id));
        }
        return owners;
    }

    public async Task<OwnerDetailReadModel?> GetOwnerAsync(string ownerId, CancellationToken ct)
    {
        var owner = await db.Set<Owner>().AsNoTracking()
            .Where(o => o.Id == ownerId)
            .Select(o => new OwnerSummaryReadModel(
                o.Id, o.FullName, o.PhonePrimary, o.PhoneSecondary, o.NationalId, o.Address, o.Gender,
                o.RegisteredUtc, db.Set<Pet>().Count(p => p.OwnerId == o.Id)))
            .FirstOrDefaultAsync(ct);
        if (owner is null) return null;

        var pets = await db.Set<Pet>().AsNoTracking()
            .Where(p => p.OwnerId == ownerId).OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Select(p => new PetSummaryReadModel(
                p.Id, p.Name, p.Species, p.Breed, p.Color, p.Gender, p.Status, p.OwnerId, p.BirthDateUtc,
                db.Set<WeightEntry>().Where(w => w.PetId == p.Id)
                    .OrderByDescending(w => w.MeasuredUtc).Select(w => (decimal?)w.WeightKg).FirstOrDefault(),
                null, null, p.MicrochipCode))
            .ToListAsync(ct);
        return new OwnerDetailReadModel(owner, pets);
    }

    public async Task<PetDetailReadModel?> GetPetAsync(string petId, CancellationToken ct)
    {
        var pet = await db.Set<Pet>().AsNoTracking().Where(p => p.Id == petId)
            .Select(p => new { p.Id, p.Name, p.Species, p.Breed, p.Color, p.Gender, p.Status, p.OwnerId,
                p.BirthDateUtc, p.IsNeutered, p.MicrochipCode, p.MicrochipImplantUtc, p.AllergyNotes, p.ChronicNotes })
            .FirstOrDefaultAsync(ct);
        if (pet is null) return null;

        var weights = await db.Set<WeightEntry>().AsNoTracking()
            .Where(w => w.PetId == petId).OrderBy(w => w.MeasuredUtc)
            .Select(w => new PetWeightReadModel(w.MeasuredUtc, w.WeightKg)).ToListAsync(ct);

        return new PetDetailReadModel(pet.Id, pet.Name, pet.Species, pet.Breed, pet.Color, pet.Gender,
            pet.Status, pet.OwnerId, pet.BirthDateUtc, pet.IsNeutered, pet.MicrochipCode,
            pet.MicrochipImplantUtc, pet.AllergyNotes, pet.ChronicNotes, weights);
    }
}
