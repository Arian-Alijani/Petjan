using Animora.Application.Abstractions;
using Animora.Application.Security;
using Animora.Application.UseCases.Patients;
using Animora.Contracts.Patients;
using Microsoft.Extensions.DependencyInjection;
using CWeightEntry = Animora.Contracts.Patients.WeightEntry;

namespace Animora.Client.Services.Patients;

/// <summary>Real IPatientsQueryService over the C01 use cases (THE C01 pattern). Singleton; the scoped
/// dispatcher + provider are resolved from a fresh async scope per call, never captured. No EF/SQL here.</summary>
public sealed class PatientsService(IServiceScopeFactory scopes) : IPatientsQueryService
{
    private const int MaxPageSize = 200; // QueryPaging.HardMaxPageSize

    public async Task<IReadOnlyList<PetSummary>> GetPetsAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var qc = provider.BuildQueryContext();
        if (qc.IsFailure) return [];
        // Unpaged Contracts member → page 1 at the max page size (documented; CONVENTIONS §10 item 2).
        var r = await dispatcher.QueryAsync(new PetDirectoryQuery(null, null, null, 1, MaxPageSize), qc.Value!);
        return r.IsSuccess ? r.Value!.Items.Select(x => x.ToSummary()).ToList() : [];
    }

    public async Task<IReadOnlyList<OwnerSummary>> GetOwnersAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var qc = provider.BuildQueryContext();
        if (qc.IsFailure) return [];
        var r = await dispatcher.QueryAsync(new OwnerDirectoryQuery(null, 1, MaxPageSize), qc.Value!);
        return r.IsSuccess ? r.Value!.Items.Select(x => x.ToSummary()).ToList() : [];
    }

    public async Task<OwnerDetail?> GetOwnerAsync(string id)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var qc = provider.BuildQueryContext();
        if (qc.IsFailure) return null;
        var r = await dispatcher.QueryAsync(new OwnerDetailQuery(id), qc.Value!);
        return r.IsSuccess && r.Value is { } m ? m.ToDetail() : null;
    }

    public async Task<PetDetail?> GetPetAsync(string id)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var qc = provider.BuildQueryContext();
        if (qc.IsFailure) return null;
        var r = await dispatcher.QueryAsync(new PetDetailQuery(id), qc.Value!);
        return r.IsSuccess && r.Value is { } m ? m.ToDetail() : null;
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
