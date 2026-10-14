using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Text;
using Animora.Application.UseCases.Patients.ReadModels;
using Animora.Contracts.Diagnostics;
using Animora.Domain.Clinical;
using Animora.Domain.Identity;
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
        TraceLog.Info($"SQL GetPetsAsync — search='{search}', species={species?.ToString() ?? "(all)"}, sort={sort}, page={page}, pageSize={pageSize}", "sql.patients");
        var filtered = FilterPets(db.Set<Pet>().AsNoTracking(), search, species);

        // Stable ORDER BY with a unique id tiebreaker so Skip/Take never drifts. "Recent" stays name-ordered
        // (same as Name); the last-visit columns below are display-only.
        var ordered = sort switch
        {
            PatientDirectorySort.Age => filtered.OrderBy(p => p.BirthDateUtc).ThenBy(p => p.Id),
            _ => filtered.OrderBy(p => p.Name).ThenBy(p => p.Id),
        };

        // Perf note (P07/P08): the latest-weight and pet-count projections below are per-row correlated
        // subqueries, index-backed by IX_WeightEntries_PetId_MeasuredUtc / IX_Pets_TenantId_OwnerId
        // (index seek + top-1 / index count, not scans). Denormalizing Pet.CurrentWeightKg and
        // Owner.PetCount is the future optimization if a measured hot path appears; deferred on purpose
        // (page size is bounded, local single-clinic DB).
        var projected = ordered
            .Select(p => new PetSummaryReadModel(
                p.Id, p.Name, p.Species, p.Breed, p.Color, p.Gender, p.Status, p.OwnerId, p.BirthDateUtc,
                db.Set<WeightEntry>().Where(w => w.PetId == p.Id)
                    .OrderByDescending(w => w.MeasuredUtc).Select(w => (decimal?)w.WeightKg).FirstOrDefault(),
                // Latest non-cancelled visit (same source/ordering as GetOwnerClinicalActivityAsync) — correlated
                // top-1 subqueries, index-backed by the Visits pet lookup. (LastVisitUtc / LastVisitType)
                db.Set<Visit>().Where(v => v.PetId == p.Id && v.State != VisitState.Cancelled)
                    .OrderByDescending(v => v.StartedUtc).Select(v => (DateTimeOffset?)v.StartedUtc).FirstOrDefault(),
                db.Set<Visit>().Where(v => v.PetId == p.Id && v.State != VisitState.Cancelled)
                    .OrderByDescending(v => v.StartedUtc).Select(v => v.Reason.ToString()).FirstOrDefault(),
                p.MicrochipCode));
        var result = await projected.ToPagedAsync(page, pageSize, Options, ct);
        TraceLog.Info($"SQL GetPetsAsync ← {result.Items.Count} rows, TotalCount={result.TotalCount} (of Pets table for tenant).", "sql.patients");
        return result;
    }

    public async Task<PagedResult<OwnerSummaryReadModel>> GetOwnersAsync(
        string? search, PetSpecies? species, PatientDirectorySort sort,
        int page, int pageSize, CancellationToken ct)
    {
        TraceLog.Info($"SQL GetOwnersAsync — search='{search}', species={species?.ToString() ?? "(all)"}, sort={sort}, page={page}, pageSize={pageSize}", "sql.patients");
        var owners = FilterOwners(search, species);

        var ordered = sort switch
        {
            PatientDirectorySort.Name => owners.OrderBy(o => o.FullName).ThenBy(o => o.Id),
            PatientDirectorySort.Age => owners.OrderBy(o => o.RegisteredUtc).ThenBy(o => o.Id),
            _ => owners.OrderByDescending(o => o.RegisteredUtc).ThenBy(o => o.Id),
        };

        // Perf note (P07/P08): the latest-weight and pet-count projections below are per-row correlated
        // subqueries, index-backed by IX_WeightEntries_PetId_MeasuredUtc / IX_Pets_TenantId_OwnerId
        // (index seek + top-1 / index count, not scans). Denormalizing Pet.CurrentWeightKg and
        // Owner.PetCount is the future optimization if a measured hot path appears; deferred on purpose
        // (page size is bounded, local single-clinic DB).
        var projected = ordered
            .Select(o => new OwnerSummaryReadModel(
                o.Id, o.FullName, o.PhonePrimary, o.PhoneSecondary, o.NationalId, o.Address, o.Gender,
                o.RegisteredUtc,
                db.Set<Pet>().Count(p => p.OwnerId == o.Id)));
        var result = await projected.ToPagedAsync(page, pageSize, Options, ct);
        TraceLog.Info($"SQL GetOwnersAsync ← {result.Items.Count} rows, TotalCount={result.TotalCount} (of Owners table for tenant).", "sql.patients");
        return result;
    }

    public async Task<PatientDirectoryFacetsReadModel> GetFacetsAsync(
        string? search, PetSpecies? species, CancellationToken ct)
    {
        TraceLog.Info($"SQL GetFacetsAsync — search='{search}', species={species?.ToString() ?? "(all)"}", "sql.patients");
        // Per-species chip counts ignore the species filter (chips show the full taxonomy under
        // the current text filter) — one grouped COUNT, not N queries.
        var speciesCounts = await FilterPets(db.Set<Pet>().AsNoTracking(), search, species: null)
            .GroupBy(p => p.Species)
            .Select(g => new { Species = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Species, x => x.Count, ct);

        // Pet total under the active search/species filter is derived from the groups above
        // (sum of all groups, or the single selected group) — no separate COUNT statement.
        var totalPets = species is { } sp
            ? speciesCounts.GetValueOrDefault(sp)
            : speciesCounts.Values.Sum();

        // Owners are cross-filtered to those with a matching pet, exactly like GetOwnersAsync.
        var totalOwners = await FilterOwners(search, species).CountAsync(ct);

        TraceLog.Info($"SQL GetFacetsAsync ← totalOwners={totalOwners}, totalPets={totalPets}, speciesGroups={(speciesCounts.Count == 0 ? "(none)" : string.Join(", ", speciesCounts.Select(kv => $"{kv.Key}={kv.Value}")))}", "sql.patients");
        return new PatientDirectoryFacetsReadModel(totalOwners, totalPets, speciesCounts);
    }

    /// <summary>Shared pet filter (search + species; search is a prefix match (`term%`, index-usable)) reused by the list page and the
    /// facet/owner cross-filter so every count and every page agree on one predicate.</summary>
    private static IQueryable<Pet> FilterPets(
        IQueryable<Pet> q, string? search, PetSpecies? species)
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = PersianText.Normalize(search);
            q = q.Where(p => EF.Functions.Like(p.SearchKey, $"{term}%"));
        }
        if (species is { } sp) q = q.Where(p => p.Species == sp);
        return q;
    }

    /// <summary>Owner filter: prefix match (`term%`, index-usable) on the owner's SearchKey, plus — when a species
    /// filter is active — an EXISTS narrowing to owners with at least one matching pet (the
    /// owner⇄pet cross-filter runs in SQL, never client-side).</summary>
    private IQueryable<Owner> FilterOwners(string? search, PetSpecies? species)
    {
        var owners = db.Set<Owner>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = PersianText.Normalize(search);
            owners = owners.Where(o => EF.Functions.Like(o.SearchKey, $"{term}%"));
        }
        if (species is not null)
        {
            var matchingPets = FilterPets(db.Set<Pet>().AsNoTracking(), search: null, species);
            owners = owners.Where(o => matchingPets.Any(p => p.OwnerId == o.Id));
        }
        return owners;
    }

    public async Task<OwnerClinicalActivityReadModel> GetOwnerClinicalActivityAsync(
        string ownerId, int take, CancellationToken ct)
    {
        var visits = db.Set<Visit>().AsNoTracking()
            .Where(v => v.OwnerId == ownerId && v.State != VisitState.Cancelled);
        var doses = from v in db.Set<VaccinationRecord>().AsNoTracking()
                    join p in db.Set<Pet>().AsNoTracking() on v.PetId equals p.Id
                    where p.OwnerId == ownerId
                    select v;

        var visitCount = await visits.CountAsync(ct);
        var doseCount = await doses.CountAsync(ct);

        // Each source is bounded to `take` newest rows; the merged list is trimmed to `take` below.
        var visitRows = await (from v in visits
                               join p in db.Set<Pet>().AsNoTracking() on v.PetId equals p.Id
                               orderby v.StartedUtc descending, v.Id
                               select new OwnerClinicalEventRow(v.Id, true, p.Id, p.Name, v.Reason.ToString(),
                                   v.Diagnosis ?? v.ChiefComplaint, v.StartedUtc))
            .Take(take).ToListAsync(ct);
        var doseRows = await (from v in doses
                              join p in db.Set<Pet>().AsNoTracking() on v.PetId equals p.Id
                              orderby v.AdministeredUtc descending, v.Id
                              select new OwnerClinicalEventRow(v.Id, false, p.Id, p.Name, v.VaccineName,
                                  null, v.AdministeredUtc, v.NextDueUtc))
            .Take(take).ToListAsync(ct);

        var merged = visitRows.Concat(doseRows)
            .OrderByDescending(e => e.OccurredUtc).ThenBy(e => e.Id).Take(take).ToList();
        TraceLog.Info($"SQL GetOwnerClinicalActivityAsync ← visitCount={visitCount}, doseCount={doseCount}, mergedRows={merged.Count}", "sql.patients");
        return new OwnerClinicalActivityReadModel(merged, visitCount, doseCount);
    }

    public async Task<OwnerDetailReadModel?> GetOwnerAsync(string ownerId, CancellationToken ct)
    {
        TraceLog.Info($"SQL GetOwnerAsync — ownerId={ownerId}", "sql.patients");
        var owner = await db.Set<Owner>().AsNoTracking()
            .Where(o => o.Id == ownerId)
            .Select(o => new OwnerSummaryReadModel(
                o.Id, o.FullName, o.PhonePrimary, o.PhoneSecondary, o.NationalId, o.Address, o.Gender,
                o.RegisteredUtc, db.Set<Pet>().Count(p => p.OwnerId == o.Id)))
            .FirstOrDefaultAsync(ct);
        if (owner is null)
        {
            TraceLog.Warn($"SQL GetOwnerAsync ← NULL — no Owner row with Id={ownerId} for this tenant (deleted, wrong tenant, or never materialised).", "sql.patients");
            return null;
        }

        var pets = await db.Set<Pet>().AsNoTracking()
            .Where(p => p.OwnerId == ownerId).OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Select(p => new PetSummaryReadModel(
                p.Id, p.Name, p.Species, p.Breed, p.Color, p.Gender, p.Status, p.OwnerId, p.BirthDateUtc,
                db.Set<WeightEntry>().Where(w => w.PetId == p.Id)
                    .OrderByDescending(w => w.MeasuredUtc).Select(w => (decimal?)w.WeightKg).FirstOrDefault(),
                // Latest non-cancelled visit (same source/ordering as GetOwnerClinicalActivityAsync) — correlated
                // top-1 subqueries, index-backed by the Visits pet lookup. (LastVisitUtc / LastVisitType)
                db.Set<Visit>().Where(v => v.PetId == p.Id && v.State != VisitState.Cancelled)
                    .OrderByDescending(v => v.StartedUtc).Select(v => (DateTimeOffset?)v.StartedUtc).FirstOrDefault(),
                db.Set<Visit>().Where(v => v.PetId == p.Id && v.State != VisitState.Cancelled)
                    .OrderByDescending(v => v.StartedUtc).Select(v => v.Reason.ToString()).FirstOrDefault(),
                p.MicrochipCode))
            .ToListAsync(ct);
        TraceLog.Info($"SQL GetOwnerAsync ← Ok: owner='{owner.FullName}', pets={pets.Count}.", "sql.patients");
        return new OwnerDetailReadModel(owner, pets);
    }

    public async Task<PetDetailReadModel?> GetPetAsync(string petId, CancellationToken ct)
    {
        TraceLog.Info($"SQL GetPetAsync — petId={petId}", "sql.patients");
        var pet = await db.Set<Pet>().AsNoTracking().Where(p => p.Id == petId)
            .Select(p => new { p.Id, p.Name, p.Species, p.Breed, p.Color, p.Gender, p.Status, p.OwnerId,
                p.BirthDateUtc, p.IsNeutered, p.MicrochipCode, p.MicrochipImplantUtc, p.AllergyNotes, p.ChronicNotes })
            .FirstOrDefaultAsync(ct);
        if (pet is null)
        {
            TraceLog.Warn($"SQL GetPetAsync ← NULL — no Pet row with Id={petId} for this tenant (deleted, wrong tenant, or never materialised).", "sql.patients");
            return null;
        }

        var weights = await db.Set<WeightEntry>().AsNoTracking()
            .Where(w => w.PetId == petId).OrderBy(w => w.MeasuredUtc)
            .Select(w => new PetWeightReadModel(w.MeasuredUtc, w.WeightKg)).ToListAsync(ct);

        var visits = await GetPetVisitsAsync(petId, ct);

        TraceLog.Info($"SQL GetPetAsync ← Ok: pet='{pet.Name}', ownerId={pet.OwnerId}, weights={weights.Count}, visits={visits.Count}.", "sql.patients");
        return new PetDetailReadModel(pet.Id, pet.Name, pet.Species, pet.Breed, pet.Color, pet.Gender,
            pet.Status, pet.OwnerId, pet.BirthDateUtc, pet.IsNeutered, pet.MicrochipCode,
            pet.MicrochipImplantUtc, pet.AllergyNotes, pet.ChronicNotes, weights, visits);
    }

    private const int PetVisitTake = 50;

    /// <summary>Newest <see cref="PetVisitTake"/> non-cancelled visits of the pet (index IX_Visits pet lookup).
    /// Entities are materialised un-tracked because prescriptions/service lines are JSON columns; the doctor
    /// display names are resolved in one extra bounded query instead of a per-row join.</summary>
    private async Task<IReadOnlyList<PetVisitReadModel>> GetPetVisitsAsync(string petId, CancellationToken ct)
    {
        var rows = await db.Set<Visit>().AsNoTracking()
            .Where(v => v.PetId == petId && v.State != VisitState.Cancelled)
            .OrderByDescending(v => v.StartedUtc).ThenBy(v => v.Id)
            .Take(PetVisitTake).ToListAsync(ct);
        if (rows.Count == 0) return [];

        var doctorIds = rows.Select(v => v.DoctorUserId).Distinct().ToList();
        var doctors = await db.Set<User>().AsNoTracking().Where(u => doctorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName }).ToListAsync(ct);
        var names = doctors.ToDictionary(d => d.Id, d => d.DisplayName);

        return rows.Select(v => new PetVisitReadModel(
            v.Id, v.Reason.ToString(), v.CompletedUtc ?? v.StartedUtc,
            names.GetValueOrDefault(v.DoctorUserId, string.Empty), v.ChiefComplaint, v.DoctorNotes, v.Diagnosis,
            v.Prescriptions.Select(p => p.DrugName).ToList(), v.Vitals?.WeightKg, v.Vitals?.TemperatureC,
            SubtotalOf(v), v.InvoiceId)).ToList();
    }

    private static long SubtotalOf(Visit v)
    {
        try
        {
            return v.ServiceLines.Aggregate(0L,
                (total, line) => checked(total + checked((long)(line.UnitPrice.Minor * line.Quantity.Value))));
        }
        catch (OverflowException)
        {
            return 0;
        }
    }
}
