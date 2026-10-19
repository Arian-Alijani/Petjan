using Animora.Application.Abstractions;
using Animora.Application.Security;
using Animora.Application.Ports;
using Animora.Application.UseCases.Patients;
using Animora.Application.UseCases.Sales;
using Animora.Application.UseCases.Vaccination;
using Animora.Application.UseCases.Patients.ReadModels;
using Animora.Domain.Patients;
using Animora.Domain.Results;
using Animora.Contracts.Diagnostics;
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
        {
            TraceLog.Info($"GetPetsPageAsync → SHORT-CIRCUIT (VaccineFilter.Has has no data source) → empty page.", "svc.patients");
            return PatientsPage<PetSummary>.Empty(query.Page, ClampPageSize(query.PageSize));
        }

        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var qc = provider.BuildQueryContext();
        if (qc.IsFailure)
        {
            TraceLog.Warn($"GetPetsPageAsync → BuildQueryContext FAILED ({qc.Error}) — throwing ServiceException.", "svc.patients");
            throw new ServiceException(qc.Error);
        }

        TraceLog.Info($"GetPetsPageAsync → PetDirectoryQuery(search='{query.Search}', species={query.Species?.ToString() ?? "(all)"}, sort={query.Sort}, page={query.Page}, pageSize={ClampPageSize(query.PageSize)}); tenant={qc.Value!.TenantId}, user={qc.Value!.UserId}", "svc.patients");
        var r = await dispatcher.QueryAsync(
            new PetDirectoryQuery(query.Search, MapSpecies(query.Species), MapSort(query.Sort),
                query.Page, ClampPageSize(query.PageSize)),
            qc.Value!);
        if (r.IsFailure)
        {
            TraceLog.Error($"GetPetsPageAsync → dispatcher FAILED ({r.Error}) detail='{r.Detail}' — throwing ServiceException.", null, "svc.patients");
            throw new ServiceException(r.Error);
        }
        var page = ToPage(r.Value!, x => x.ToSummary());
        TraceLog.Info($"GetPetsPageAsync ← Ok: items={page.Items.Count}, totalCount={page.TotalCount}.", "svc.patients");
        return page;
    }

    public async Task<PatientsPage<OwnerSummary>> GetOwnersPageAsync(PatientsDirectoryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Vaccine == VaccineFilter.Has)
        {
            TraceLog.Info($"GetOwnersPageAsync → SHORT-CIRCUIT (VaccineFilter.Has has no data source) → empty page.", "svc.patients");
            return PatientsPage<OwnerSummary>.Empty(query.Page, ClampPageSize(query.PageSize));
        }

        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var qc = provider.BuildQueryContext();
        if (qc.IsFailure)
        {
            TraceLog.Warn($"GetOwnersPageAsync → BuildQueryContext FAILED ({qc.Error}) — throwing ServiceException.", "svc.patients");
            throw new ServiceException(qc.Error);
        }

        TraceLog.Info($"GetOwnersPageAsync → OwnerDirectoryQuery(search='{query.Search}', species={query.Species?.ToString() ?? "(all)"}, sort={query.Sort}, page={query.Page}, pageSize={ClampPageSize(query.PageSize)}); tenant={qc.Value!.TenantId}, user={qc.Value!.UserId}", "svc.patients");
        var r = await dispatcher.QueryAsync(
            new OwnerDirectoryQuery(query.Search, MapSpecies(query.Species), MapSort(query.Sort),
                query.Page, ClampPageSize(query.PageSize)),
            qc.Value!);
        if (r.IsFailure)
        {
            TraceLog.Error($"GetOwnersPageAsync → dispatcher FAILED ({r.Error}) detail='{r.Detail}' — throwing ServiceException.", null, "svc.patients");
            throw new ServiceException(r.Error);
        }
        var page = ToPage(r.Value!, x => x.ToSummary());
        TraceLog.Info($"GetOwnersPageAsync ← Ok: items={page.Items.Count}, totalCount={page.TotalCount}.", "svc.patients");
        return page;
    }

    public async Task<PatientsDirectoryFacets> GetDirectoryFacetsAsync(PatientsDirectoryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        // "Has vaccines" matches nothing (no source yet) — report zeros without a DB round-trip.
        if (query.Vaccine == VaccineFilter.Has)
        {
            TraceLog.Info($"GetDirectoryFacetsAsync → SHORT-CIRCUIT (VaccineFilter.Has) → zero facets.", "svc.patients");
            return new PatientsDirectoryFacets(0, 0, new Dictionary<PetSpecies, int>());
        }

        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var qc = provider.BuildQueryContext();
        if (qc.IsFailure)
        {
            TraceLog.Warn($"GetDirectoryFacetsAsync → BuildQueryContext FAILED ({qc.Error}) — throwing ServiceException.", "svc.patients");
            throw new ServiceException(qc.Error);
        }

        TraceLog.Info($"GetDirectoryFacetsAsync → PatientsDirectoryFacetsQuery(search='{query.Search}', species={query.Species?.ToString() ?? "(all)"})", "svc.patients");
        var r = await dispatcher.QueryAsync(
            new PatientsDirectoryFacetsQuery(query.Search, MapSpecies(query.Species)), qc.Value!);
        if (r.IsFailure)
        {
            TraceLog.Error($"GetDirectoryFacetsAsync → dispatcher FAILED ({r.Error}) detail='{r.Detail}' — throwing ServiceException.", null, "svc.patients");
            throw new ServiceException(r.Error);
        }
        var m = r.Value!;
        var species = m.SpeciesCounts.ToDictionary(kv => MapSpeciesToContract(kv.Key), kv => kv.Value);
        var facets = new PatientsDirectoryFacets(m.TotalOwners, m.TotalPets, species);
        TraceLog.Info($"GetDirectoryFacetsAsync ← Ok: totalOwners={facets.TotalOwners}, totalPets={facets.TotalPets}.", "svc.patients");
        return facets;
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

    public async Task<OwnerDetail?> GetOwnerAsync(string id)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var qc = provider.BuildQueryContext();
        if (qc.IsFailure) throw new ServiceException(qc.Error);
        var r = await dispatcher.QueryAsync(new OwnerDetailQuery(id), qc.Value!);
        if (r.IsFailure) throw new ServiceException(r.Error);
        // Success + null value is a genuine not-found; auth/session failures throw above.
        return r.Value is { } m ? m.ToDetail() : null;
    }

    public async Task<OwnerActivityFeed> GetOwnerActivityAsync(string ownerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId)) return Empty;
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var qc = provider.BuildQueryContext();
        if (qc.IsFailure) throw new ServiceException(qc.Error);
        var zone = scope.ServiceProvider.GetRequiredService<IClinicTimeZoneProvider>().GetClinicTimeZone();
        DateTime Local(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, zone).DateTime;

        // Sequential on one scope (a DbContext is not thread-safe). A source the current user may not read
        // (permission failure) simply contributes nothing — it never hides the other sections.
        var clinical = await dispatcher.QueryAsync(new OwnerClinicalActivityQuery(ownerId, Take), qc.Value!);
        var invoices = await dispatcher.QueryAsync(new OwnerInvoicesQuery(ownerId, Take), qc.Value!);
        var sms = await dispatcher.QueryAsync(new OwnerSmsRemindersQuery(ownerId, Take), qc.Value!);

        var entries = new List<OwnerActivityEntry>();
        var visitCount = 0;
        var vaccineCount = 0;
        if (clinical.IsSuccess && clinical.Value is { } c)
        {
            visitCount = c.VisitCount;
            vaccineCount = c.VaccineCount;
            foreach (var e in c.Events)
            {
                entries.Add(e.IsVisit
                    ? new OwnerActivityEntry(Local(e.OccurredUtc), OwnerActivityKind.Visit,
                        $"ویزیت {VisitReasonLabel(e.Code)} · {e.PetName}",
                        string.IsNullOrWhiteSpace(e.Detail) ? "ویزیت ثبت شد" : e.Detail!, null, e.PetId)
                    : new OwnerActivityEntry(Local(e.OccurredUtc), OwnerActivityKind.Vaccine,
                        $"واکسن {e.Code} · {e.PetName}",
                        e.NextDueUtc is { } due ? $"نوبت بعدی: {Local(due):yyyy/MM/dd}" : "دوز تزریق شد",
                        null, e.PetId));
            }
        }

        var invoiceCount = 0;
        if (invoices.IsSuccess && invoices.Value is { } inv)
        {
            invoiceCount = inv.Count;
            foreach (var i in inv)
            {
                var number = string.IsNullOrWhiteSpace(i.Number) ? i.Id : i.Number;
                entries.Add(new OwnerActivityEntry(Local(i.IssuedUtc), OwnerActivityKind.Invoice,
                    $"فاکتور {number}", string.IsNullOrWhiteSpace(i.PetName) ? "فاکتور ثبت شد" : i.PetName,
                    i.TotalMinor, i.Id));
            }
        }

        var smsCount = 0;
        if (sms.IsSuccess && sms.Value is { } messages)
        {
            smsCount = messages.Count;
            foreach (var m in messages)
            {
                entries.Add(new OwnerActivityEntry(Local(m.SentUtc), OwnerActivityKind.Sms,
                    string.IsNullOrWhiteSpace(m.VaccineName) ? $"پیامک یادآوری · {m.PetName}"
                        : $"پیامک یادآوری {m.VaccineName} · {m.PetName}",
                    string.IsNullOrWhiteSpace(m.Body) ? "پیامک ارسال شد" : m.Body!, null, m.PetId));
            }
        }

        var merged = entries.OrderByDescending(e => e.When).Take(Take).ToArray();
        return new OwnerActivityFeed(merged, visitCount, vaccineCount, invoiceCount, smsCount,
            merged.Length > 0 ? merged[0].When : null);
    }

    private const int Take = 50;

    private static readonly OwnerActivityFeed Empty = new([], 0, 0, 0, 0, null);

    private static string VisitReasonLabel(string reason) => PatientsMapping.VisitReasonLabel(reason);

    public async Task<PetDetail?> GetPetAsync(string id)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var qc = provider.BuildQueryContext();
        if (qc.IsFailure) throw new ServiceException(qc.Error);
        var r = await dispatcher.QueryAsync(new PetDetailQuery(id), qc.Value!);
        if (r.IsFailure) throw new ServiceException(r.Error);
        // Success + null value is a genuine not-found; auth/session failures throw above.
        return r.Value is { } m ? m.ToDetail() : null;
    }

    // ── Write methods ─────────────────────────────────────────────────────────────────────────
    // Perf note (P21): every write below ends with a read-after-write (GetOwnerAsync/GetPetAsync) on
    // purpose — callers need the fresh DTO, and the command handlers return only Unit/id today.
    // The re-read is index-backed (IX_WeightEntries_PetId_MeasuredUtc for the last-weight subquery,
    // IX_Pets_TenantId_OwnerId for the pet count). Returning the projection from the command is a
    // deferred optimization; revisit only if profiling shows a hot-path problem (e.g. weight append).

    public async Task<OwnerDetail> CreateOwnerAsync(NewOwnerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var cc = await provider.BuildCommandContextAsync(null, null, null, default);
        if (cc.IsFailure) throw new ServiceException(cc.Error);
        var p = request.Pet;
        var cmd = new CreateOwnerCommand(FullName: $"{request.FirstName} {request.LastName}".Trim(),
            request.Mobile1, request.Gender.ToDomain(), request.Mobile2, request.NationalId, request.Address,
            p?.Name, p?.Species.ToDomain(), p?.Breed, p?.Color, p?.Gender.ToDomain(),
            ToUtc(p?.BirthDate), p?.MicrochipCode, ToUtc(p?.MicrochipDate), p?.Neutered ?? false,
            p is null ? null : PatientsMapping.JoinList(p.Allergies ?? []),
            p is null ? null : PatientsMapping.JoinList(p.ChronicConditions ?? []),
            PetHabitat: NullIfBlank(p?.Habitat), PetBehavior: NullIfBlank(p?.Behavior), PetDiet: NullIfBlank(p?.Diet),
            PetBloodType: NullIfBlank(p?.BloodType), PetInsurance: NullIfBlank(p?.Insurance),
            PetMicrochipVendor: NullIfBlank(p?.MicrochipVendor), PetMicrochipLocation: NullIfBlank(p?.MicrochipLocation),
            PetNeuteredUtc: ToUtc(p?.NeuteredDate));
        var r = await dispatcher.SendAsync(cmd, cc.Value!);
        if (r.IsFailure) throw new ServiceException(r.Error);
        return (await GetOwnerAsync(r.Value!))!;
    }

    public async Task<PetDetail> CreatePetAsync(string ownerId, NewPetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var cc = await provider.BuildCommandContextAsync(null, null, null, default);
        if (cc.IsFailure) throw new ServiceException(cc.Error);
        var cmd = new CreatePetCommand(ownerId, request.Name, request.Species.ToDomain(), request.Breed,
            request.Color, request.Gender.ToDomain(), ToUtc(request.BirthDate), request.MicrochipCode,
            ToUtc(request.MicrochipDate), request.Neutered,
            PatientsMapping.JoinList(request.Allergies ?? []), PatientsMapping.JoinList(request.ChronicConditions ?? []),
            Habitat: NullIfBlank(request.Habitat), Behavior: NullIfBlank(request.Behavior), Diet: NullIfBlank(request.Diet),
            BloodType: NullIfBlank(request.BloodType), Insurance: NullIfBlank(request.Insurance),
            MicrochipVendor: NullIfBlank(request.MicrochipVendor), MicrochipLocation: NullIfBlank(request.MicrochipLocation),
            NeuteredUtc: ToUtc(request.NeuteredDate));
        var r = await dispatcher.SendAsync(cmd, cc.Value!);
        if (r.IsFailure) throw new ServiceException(r.Error);
        return (await GetPetAsync(r.Value!))!;
    }

    public async Task<OwnerDetail> SaveOwnerAsync(OwnerDetail owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var cc = await provider.BuildCommandContextAsync(null, null, null, default);
        if (cc.IsFailure) throw new ServiceException(cc.Error);
        var cmd = new UpdateOwnerCommand(owner.Id, $"{owner.FirstName} {owner.LastName}".Trim(),
            owner.Mobile1, owner.Gender.ToDomain(), owner.Mobile2, owner.NationalId, owner.Address);
        var r = await dispatcher.SendAsync(cmd, cc.Value!);
        if (r.IsFailure) throw new ServiceException(r.Error);
        return (await GetOwnerAsync(owner.Id))!;
    }

    public async Task<PetDetail> SavePetInfoAsync(PetDetail pet)
    {
        ArgumentNullException.ThrowIfNull(pet);
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var cc = await provider.BuildCommandContextAsync(null, null, null, default);
        if (cc.IsFailure) throw new ServiceException(cc.Error);
        var cmd = new UpdatePetInfoCommand(pet.Id, pet.Name, pet.Breed, pet.Color, pet.Gender.ToDomain(),
            pet.BirthDate is { } bd ? ToUtc(bd) : null, pet.Neutered,
            PatientsMapping.JoinList(pet.Allergies), PatientsMapping.JoinList(pet.ChronicConditions),
            Habitat: NullIfBlank(pet.Habitat), Behavior: NullIfBlank(pet.Behavior), Diet: NullIfBlank(pet.Diet),
            BloodType: NullIfBlank(pet.BloodType), Insurance: NullIfBlank(pet.Insurance),
            MicrochipCode: NullIfBlank(pet.Microchip.Code),
            MicrochipUtc: pet.Microchip.ImplantDate is { } chip ? ToUtc(chip) : null,
            MicrochipVendor: NullIfBlank(pet.Microchip.Vendor),
            MicrochipLocation: NullIfBlank(pet.Microchip.Location),
            NeuteredUtc: pet.NeuteredDate is { } nd ? ToUtc(nd) : null);
        var r = await dispatcher.SendAsync(cmd, cc.Value!);
        if (r.IsFailure) throw new ServiceException(r.Error);
        return (await GetPetAsync(pet.Id))!;
    }

    public async Task<PetDetail> SavePetAlertsAsync(string petId, IReadOnlyList<string> allergies, IReadOnlyList<string> chronicConditions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(petId);
        ArgumentNullException.ThrowIfNull(allergies);
        ArgumentNullException.ThrowIfNull(chronicConditions);
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var cc = await provider.BuildCommandContextAsync(null, null, null, default);
        if (cc.IsFailure) throw new ServiceException(cc.Error);
        var current = await GetPetAsync(petId);
        if (current is null) throw new ServiceException(ErrorCodes.PetNotFound);
        var cmd = new UpdatePetInfoCommand(current.Id, current.Name, current.Breed, current.Color,
            current.Gender.ToDomain(), current.BirthDate is { } bd ? ToUtc(bd) : null, current.Neutered,
            PatientsMapping.JoinList(allergies), PatientsMapping.JoinList(chronicConditions));
        var r = await dispatcher.SendAsync(cmd, cc.Value!);
        if (r.IsFailure) throw new ServiceException(r.Error);
        return (await GetPetAsync(petId))!;
    }

    public async Task<PetDetail> AddWeightEntryAsync(string petId, CWeightEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var cc = await provider.BuildCommandContextAsync(null, null, null, default);
        if (cc.IsFailure) throw new ServiceException(cc.Error);
        var cmd = new AddWeightEntryCommand(petId, (decimal)entry.WeightKg, ToUtc(entry.Date)!.Value, null);
        var r = await dispatcher.SendAsync(cmd, cc.Value!);
        if (r.IsFailure) throw new ServiceException(r.Error);
        return (await GetPetAsync(petId))!;
    }

    public async Task<PetDetail> AddPetDocumentAsync(
        string petId, string title, string fileName, Stream content, string contentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(petId);
        ArgumentNullException.ThrowIfNull(content);
        var name = Path.GetFileName(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // 1) bytes first: content-addressed, deduplicated, local-only (mirrors the visit attachment design).
        // The stream is buffered so the size is exact even for a non-seekable picker stream.
        string hash;
        long size;
        using var buffered = new MemoryStream();
        await content.CopyToAsync(buffered);
        size = buffered.Length;
        buffered.Position = 0;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<ILocalFileStore>().PutAsync(buffered, default);
            if (stored.IsFailure) throw new ServiceException(stored.Error);
            hash = stored.Value!;
        }

        // 2) then the journaled metadata row, so the file replicates and shows in the pet file.
        await using (var scope = scopes.CreateAsyncScope())
        {
            var (dispatcher, provider) = Resolve(scope);
            var cc = await provider.BuildCommandContextAsync(null, null, null, default);
            if (cc.IsFailure) throw new ServiceException(cc.Error);
            var cmd = new AddPetDocumentCommand(petId, Title(title, name), KindFor(name, contentType), hash, size, hash);
            var r = await dispatcher.SendAsync(cmd, cc.Value!);
            if (r.IsFailure) throw new ServiceException(r.Error);
        }

        return (await GetPetAsync(petId))!;
    }

    public async Task<Stream?> OpenPetDocumentAsync(string contentHashSha256)
    {
        if (string.IsNullOrWhiteSpace(contentHashSha256)) return null;
        await using var scope = scopes.CreateAsyncScope();
        var opened = await scope.ServiceProvider.GetRequiredService<ILocalFileStore>()
            .OpenAsync(contentHashSha256, default);
        // A missing/damaged payload is a display concern (toast), not a fault — report it as null.
        return opened.IsFailure ? null : opened.Value;
    }

    /// <summary>Uses the caller's title when given, otherwise the file name — so a row always has a label.</summary>
    private static string Title(string? title, string fileName) =>
        string.IsNullOrWhiteSpace(title) ? fileName : title.Trim();

    /// <summary>Category picked from the content type / file extension so uploads land in the right tab group.</summary>
    private static DocumentKind KindFor(string fileName, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            var type = contentType.Split(';')[0].Trim().ToLowerInvariant();
            if (type.StartsWith("image/", StringComparison.Ordinal)) return DocumentKind.Imaging;
            if (type == "application/pdf") return DocumentKind.Lab;
        }

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => DocumentKind.Lab,
            ".dcm" or ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".webp" or ".tif" or ".tiff" => DocumentKind.Imaging,
            _ => DocumentKind.Other,
        };
    }

    // Deletes are soft (tombstone) and return nothing — the record is gone from every read after
    // this call, so there is no fresh DTO to hand back (no read-after-write here).

    public async Task DeletePetAsync(string petId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(petId);
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var cc = await provider.BuildCommandContextAsync(null, null, null, default);
        if (cc.IsFailure) throw new ServiceException(cc.Error);
        var r = await dispatcher.SendAsync(new DeletePetCommand(petId), cc.Value!);
        if (r.IsFailure) throw new ServiceException(r.Error);
    }

    public async Task DeleteOwnerAsync(string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);
        var cc = await provider.BuildCommandContextAsync(null, null, null, default);
        if (cc.IsFailure) throw new ServiceException(cc.Error);
        var r = await dispatcher.SendAsync(new DeleteOwnerCommand(ownerId), cc.Value!);
        if (r.IsFailure) throw new ServiceException(r.Error);
    }

    /// <summary>
    /// Wraps a UI <see cref="DateTime"/> (Kind=Unspecified) as a UTC-offset value. A value already carrying
    /// Kind=Utc/Local is re-tagged as unspecified first, because <see cref="DateTimeOffset"/> rejects an
    /// explicit offset that disagrees with the kind.
    /// </summary>
    private static DateTimeOffset? ToUtc(DateTime? d) => d is { } v
        ? new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Unspecified), TimeSpan.Zero)
        : null;

    /// <summary>Trims a free-text field, mapping a blank string to null so the DB stores "not recorded".</summary>
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static (IDispatcher Dispatcher, ICurrentUserProvider Provider) Resolve(AsyncServiceScope scope) =>
        (scope.ServiceProvider.GetRequiredService<IDispatcher>(),
         scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>());
}
