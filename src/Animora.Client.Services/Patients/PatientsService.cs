using Animora.Application.Abstractions;
using Animora.Application.Security;
using Animora.Application.UseCases.Patients;
using Animora.Application.UseCases.Patients.ReadModels;
using Animora.Domain.Results;
using Animora.Contracts.Patients;
using Microsoft.Extensions.DependencyInjection;
using CWeightEntry = Animora.Contracts.Patients.WeightEntry;

namespace Animora.Client.Services.Patients;

/// <summary>Real IPatientsQueryService over the C01 use cases (THE C01 pattern). Singleton; the scoped
/// dispatcher + provider are resolved from a fresh async scope per call, never captured. No EF/SQL here.</summary>
public sealed class PatientsService(IServiceScopeFactory scopes) : IPatientsQueryService
{
    private const int MaxPageSize = 200; // QueryPaging.HardMaxPageSize

    // ── Server-side directory reads (the C01 scalability path) ───────────────────────────────────
    // Search/filter/sort/paging are pushed all the way to SQL; each call returns exactly ONE bounded
    // page (2 SQL commands: COUNT + LIMIT/OFFSET). The whole-table read loop is gone.

    public async Task<PatientsPage<PetSummary>> GetPetsPageAsync(PatientsDirectoryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        // "Has vaccines" has no data source yet (C01) — it can only ever match zero pets, so short
        // out without a DB round-trip rather than returning a misleading full list.
        if (query.Vaccine == VaccineFilter.Has)
            return PatientsPage<PetSummary>.Empty(query.Page, ClampPageSize(query.PageSize));

        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var qc = provider.BuildQueryContext();
        if (qc.IsFailure) throw new InvalidOperationException(qc.Error.Code);

        var r = await dispatcher.QueryAsync(
            new PetDirectoryQuery(query.Search, MapSpecies(query.Species), MapSort(query.Sort),
                query.Page, ClampPageSize(query.PageSize)),
            qc.Value!);
        if (r.IsFailure) throw new InvalidOperationException(r.Error.Code);
        return ToPage(r.Value!, x => x.ToSummary());
    }

    public async Task<PatientsPage<OwnerSummary>> GetOwnersPageAsync(PatientsDirectoryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Vaccine == VaccineFilter.Has)
            return PatientsPage<OwnerSummary>.Empty(query.Page, ClampPageSize(query.PageSize));

        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var qc = provider.BuildQueryContext();
        if (qc.IsFailure) throw new InvalidOperationException(qc.Error.Code);

        var r = await dispatcher.QueryAsync(
            new OwnerDirectoryQuery(query.Search, MapSpecies(query.Species), MapSort(query.Sort),
                query.Page, ClampPageSize(query.PageSize)),
            qc.Value!);
        if (r.IsFailure) throw new InvalidOperationException(r.Error.Code);
        return ToPage(r.Value!, x => x.ToSummary());
    }

    public async Task<PatientsDirectoryFacets> GetDirectoryFacetsAsync(PatientsDirectoryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        // "Has vaccines" matches nothing (no source yet) — report zeros without a DB round-trip.
        if (query.Vaccine == VaccineFilter.Has)
            return new PatientsDirectoryFacets(0, 0, new Dictionary<PetSpecies, int>());

        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var qc = provider.BuildQueryContext();
        if (qc.IsFailure) throw new InvalidOperationException(qc.Error.Code);

        var r = await dispatcher.QueryAsync(
            new PatientsDirectoryFacetsQuery(query.Search, MapSpecies(query.Species)), qc.Value!);
        if (r.IsFailure) throw new InvalidOperationException(r.Error.Code);
        var m = r.Value!;
        var species = m.SpeciesCounts.ToDictionary(kv => MapSpeciesToContract(kv.Key), kv => kv.Value);
        return new PatientsDirectoryFacets(m.TotalOwners, m.TotalPets, species);
    }

    // ── Legacy whole-directory enumeration (add-patient wizard's in-modal lookup only) ───────────
    // Bounded by MaxPageSize; the directory page no longer calls these.

    public async Task<IReadOnlyList<PetSummary>> GetPetsAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var qc = provider.BuildQueryContext();
        if (qc.IsFailure) throw new InvalidOperationException(qc.Error.Code);
        return await ReadDirectoryAsync(
            page => dispatcher.QueryAsync(
                new PetDirectoryQuery(null, null, PatientDirectorySort.Name, page, MaxPageSize), qc.Value!),
            x => x.ToSummary());
    }

    public async Task<IReadOnlyList<OwnerSummary>> GetOwnersAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var qc = provider.BuildQueryContext();
        if (qc.IsFailure) throw new InvalidOperationException(qc.Error.Code);
        return await ReadDirectoryAsync(
            page => dispatcher.QueryAsync(
                new OwnerDirectoryQuery(null, null, PatientDirectorySort.Name, page, MaxPageSize), qc.Value!),
            x => x.ToSummary());
    }

    private static int ClampPageSize(int pageSize) => Math.Clamp(pageSize, 1, MaxPageSize);

    private static PatientDirectorySort MapSort(PatientsSortOrder sort) => sort switch
    {
        PatientsSortOrder.Name => PatientDirectorySort.Name,
        PatientsSortOrder.Age => PatientDirectorySort.Age,
        _ => PatientDirectorySort.Recent,
    };

    // Contracts → Domain species for the server-side filter (bijective with the write path).
    private static Animora.Domain.Patients.PetSpecies? MapSpecies(PetSpecies? s) =>
        s is { } v ? v.ToDomain() : null;

    private static PetSpecies MapSpeciesToContract(Animora.Domain.Patients.PetSpecies s) => s switch
    {
        Animora.Domain.Patients.PetSpecies.Dog => PetSpecies.Dog,
        Animora.Domain.Patients.PetSpecies.Cat => PetSpecies.Cat,
        Animora.Domain.Patients.PetSpecies.Bird => PetSpecies.Bird,
        Animora.Domain.Patients.PetSpecies.Rodent => PetSpecies.Rodent,
        Animora.Domain.Patients.PetSpecies.Reptile => PetSpecies.Reptile,
        Animora.Domain.Patients.PetSpecies.Rabbit => PetSpecies.Rabbit,
        _ => PetSpecies.Other,
    };

    private static PatientsPage<TSummary> ToPage<TModel, TSummary>(
        PagedResult<TModel> batch, Func<TModel, TSummary> map)
    {
        var items = batch.Items.Select(map).ToArray();
        return new PatientsPage<TSummary>(items, batch.TotalCount, batch.Page, batch.PageSize);
    }

    // The Contracts directory is unpaged, but each database read remains bounded. Follow the
    // EFFECTIVE page size (which can be configured below 200), not the requested size or item count.
    // Never turn a failed later page into a successful-looking partial directory.
    private static async Task<IReadOnlyList<TSummary>> ReadDirectoryAsync<TModel, TSummary>(
        Func<int, Task<Result<PagedResult<TModel>>>> readPage, Func<TModel, TSummary> map)
    {
        var items = new List<TSummary>();
        for (var page = 1; ; page++)
        {
            var result = await readPage(page).ConfigureAwait(false);
            if (result.IsFailure) throw new InvalidOperationException(result.Error.Code);
            var batch = result.Value!;
            if (batch.Page != page || batch.PageSize < 1)
                throw new InvalidOperationException("Invalid directory paging metadata.");
            foreach (var item in batch.Items) items.Add(map(item));
            if ((long)batch.Page * batch.PageSize >= batch.TotalCount) return items;
            if (batch.Items.Count == 0)
                throw new InvalidOperationException("Directory ended before its reported total.");
        }
    }

    public async Task<OwnerDetail?> GetOwnerAsync(string id)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var qc = provider.BuildQueryContext();
        if (qc.IsFailure) throw new InvalidOperationException(qc.Error.Code);
        var r = await dispatcher.QueryAsync(new OwnerDetailQuery(id), qc.Value!);
        if (r.IsFailure) throw new InvalidOperationException(r.Error.Code);
        // Success + null value is a genuine not-found; auth/session failures throw above.
        return r.Value is { } m ? m.ToDetail() : null;
    }

    public async Task<PetDetail?> GetPetAsync(string id)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var qc = provider.BuildQueryContext();
        if (qc.IsFailure) throw new InvalidOperationException(qc.Error.Code);
        var r = await dispatcher.QueryAsync(new PetDetailQuery(id), qc.Value!);
        if (r.IsFailure) throw new InvalidOperationException(r.Error.Code);
        // Success + null value is a genuine not-found; auth/session failures throw above.
        return r.Value is { } m ? m.ToDetail() : null;
    }

    public async Task<OwnerDetail> CreateOwnerAsync(NewOwnerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var cc = await provider.BuildCommandContextAsync(null, null, null, default);
        if (cc.IsFailure) throw new InvalidOperationException(cc.Error.Code);
        var p = request.Pet;
        var cmd = new CreateOwnerCommand(FullName: $"{request.FirstName} {request.LastName}".Trim(),
            request.Mobile1, request.Gender.ToDomain(), request.Mobile2, request.NationalId, request.Address,
            p?.Name, p?.Species.ToDomain(), p?.Breed, p?.Color, p?.Gender.ToDomain(),
            ToUtc(p?.BirthDate), p?.MicrochipCode, ToUtc(p?.MicrochipDate), p?.Neutered ?? false);
        var r = await dispatcher.SendAsync(cmd, cc.Value!);
        if (r.IsFailure) throw new InvalidOperationException(r.Error.Code);
        return (await GetOwnerAsync(r.Value!))!;
    }

    public async Task<PetDetail> CreatePetAsync(string ownerId, NewPetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var cc = await provider.BuildCommandContextAsync(null, null, null, default);
        if (cc.IsFailure) throw new InvalidOperationException(cc.Error.Code);
        var cmd = new CreatePetCommand(ownerId, request.Name, request.Species.ToDomain(), request.Breed,
            request.Color, request.Gender.ToDomain(), ToUtc(request.BirthDate), request.MicrochipCode,
            ToUtc(request.MicrochipDate), request.Neutered);
        var r = await dispatcher.SendAsync(cmd, cc.Value!);
        if (r.IsFailure) throw new InvalidOperationException(r.Error.Code);
        return (await GetPetAsync(r.Value!))!;
    }

    public async Task<OwnerDetail> SaveOwnerAsync(OwnerDetail owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var cc = await provider.BuildCommandContextAsync(null, null, null, default);
        if (cc.IsFailure) throw new InvalidOperationException(cc.Error.Code);
        var cmd = new UpdateOwnerCommand(owner.Id, $"{owner.FirstName} {owner.LastName}".Trim(),
            owner.Mobile1, owner.Gender.ToDomain(), owner.Mobile2, owner.NationalId, owner.Address);
        var r = await dispatcher.SendAsync(cmd, cc.Value!);
        if (r.IsFailure) throw new InvalidOperationException(r.Error.Code);
        return (await GetOwnerAsync(owner.Id))!;
    }

    public async Task<PetDetail> SavePetInfoAsync(PetDetail pet)
    {
        ArgumentNullException.ThrowIfNull(pet);
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var cc = await provider.BuildCommandContextAsync(null, null, null, default);
        if (cc.IsFailure) throw new InvalidOperationException(cc.Error.Code);
        var cmd = new UpdatePetInfoCommand(pet.Id, pet.Name, pet.Breed, pet.Color, pet.Gender.ToDomain(),
            pet.BirthDate == default ? null : ToUtc(pet.BirthDate), pet.Neutered,
            string.Join(", ", pet.Allergies), string.Join(", ", pet.ChronicConditions));
        var r = await dispatcher.SendAsync(cmd, cc.Value!);
        if (r.IsFailure) throw new InvalidOperationException(r.Error.Code);
        return (await GetPetAsync(pet.Id))!;
    }

    public async Task<PetDetail> AddWeightEntryAsync(string petId, CWeightEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var cc = await provider.BuildCommandContextAsync(null, null, null, default);
        if (cc.IsFailure) throw new InvalidOperationException(cc.Error.Code);
        var cmd = new AddWeightEntryCommand(petId, (decimal)entry.WeightKg, ToUtc(entry.Date)!.Value, null);
        var r = await dispatcher.SendAsync(cmd, cc.Value!);
        if (r.IsFailure) throw new InvalidOperationException(r.Error.Code);
        return (await GetPetAsync(petId))!;
    }

    /// <summary>
    /// Wraps a UI <see cref="DateTime"/> (Kind=Unspecified) as a UTC-offset value. A value already carrying
    /// Kind=Utc/Local is re-tagged as unspecified first, because <see cref="DateTimeOffset"/> rejects an
    /// explicit offset that disagrees with the kind.
    /// </summary>
    private static DateTimeOffset? ToUtc(DateTime? d) => d is { } v
        ? new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Unspecified), TimeSpan.Zero)
        : null;

    private static (IDispatcher Dispatcher, ICurrentUserProvider Provider) Resolve(AsyncServiceScope scope) =>
        (scope.ServiceProvider.GetRequiredService<IDispatcher>(),
         scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>());
}
