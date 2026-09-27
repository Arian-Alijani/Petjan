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
        string? search, PetSpecies? species, PetStatus? status, int page, int pageSize, CancellationToken ct)
    {
        var q = db.Set<Pet>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = PersianText.Normalize(search);
            q = q.Where(p => EF.Functions.Like(p.SearchKey, $"%{term}%"));
        }
        if (species is { } sp) q = q.Where(p => p.Species == sp);
        if (status is { } st) q = q.Where(p => p.Status == st);

        var projected = q
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Select(p => new PetSummaryReadModel(
                p.Id, p.Name, p.Species, p.Breed, p.Color, p.Gender, p.Status, p.OwnerId, p.BirthDateUtc,
                db.Set<WeightEntry>().Where(w => w.PetId == p.Id)
                    .OrderByDescending(w => w.MeasuredUtc).Select(w => (decimal?)w.WeightKg).FirstOrDefault(),
                null, null,   // LastVisitUtc / LastVisitType arrive with C03; no visit source yet.
                p.MicrochipCode));
        return await projected.ToPagedAsync(page, pageSize, Options, ct);
    }

    public async Task<PagedResult<OwnerSummaryReadModel>> GetOwnersAsync(
        string? search, int page, int pageSize, CancellationToken ct)
    {
        var q = db.Set<Owner>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = PersianText.Normalize(search);
            q = q.Where(o => EF.Functions.Like(o.SearchKey, $"%{term}%"));
        }
        var projected = q
            .OrderBy(o => o.FullName).ThenBy(o => o.Id)
            .Select(o => new OwnerSummaryReadModel(
                o.Id, o.FullName, o.PhonePrimary, o.PhoneSecondary, o.NationalId, o.Address, o.Gender,
                o.RegisteredUtc,
                db.Set<Pet>().Count(p => p.OwnerId == o.Id),
                0L)); // TotalSpend deferred to C09 OwnerFinancialSnapshot.
        return await projected.ToPagedAsync(page, pageSize, Options, ct);
    }

    public async Task<OwnerDetailReadModel?> GetOwnerAsync(string ownerId, CancellationToken ct)
    {
        var owner = await db.Set<Owner>().AsNoTracking()
            .Where(o => o.Id == ownerId)
            .Select(o => new OwnerSummaryReadModel(
                o.Id, o.FullName, o.PhonePrimary, o.PhoneSecondary, o.NationalId, o.Address, o.Gender,
                o.RegisteredUtc, db.Set<Pet>().Count(p => p.OwnerId == o.Id), 0L))
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
