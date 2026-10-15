using System;
using System.Threading.Tasks;
using System.Reflection;
using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Patients;
using Animora.Application.UseCases.Patients.ReadModels;
using Animora.Client.Services.Patients;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>
/// C01 command use cases: the journaled-command contract (type name + aggregate) and the handler rules the
/// phase locks — unknown owner → OWNER_NOT_FOUND, non-positive weight → WEIGHT_ENTRY_INVALID,
/// unknown pet → PET_NOT_FOUND — plus the validators' required-field failures.
/// </summary>
public sealed class PatientsUseCaseTests
{
    private sealed class TestClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakeOwnerRepository : IOwnerRepository
    {
        public readonly Dictionary<string, Owner> Items = new(StringComparer.Ordinal);
        public Task<Owner?> FindAsync(string id, System.Threading.CancellationToken ct = default) =>
            Task.FromResult(Items.TryGetValue(id, out var o) ? o : null);
        public Task<Owner?> FindByPhoneAsync(string phone, System.Threading.CancellationToken ct = default) =>
            Task.FromResult(Items.Values.FirstOrDefault(o => o.PhonePrimary == phone));
        public Task<Owner> RequireAsync(string id, System.Threading.CancellationToken ct = default) =>
            FindAsync(id, ct).ContinueWith(t => t.Result!);
        public void Add(Owner entity) => Items[entity.Id] = entity;
        public void AddRange(System.Collections.Generic.IEnumerable<Owner> entities)
        {
            foreach (var e in entities) Add(e);
        }
    }

    private sealed class FakePetRepository : IPetRepository
    {
        public readonly Dictionary<string, Pet> Items = new(StringComparer.Ordinal);
        public Task<Pet?> FindAsync(string id, System.Threading.CancellationToken ct = default) =>
            Task.FromResult(Items.TryGetValue(id, out var p) ? p : null);
        public Task<Pet?> FindByOwnerAndNameAsync(string ownerId, string name, System.Threading.CancellationToken ct = default) =>
            Task.FromResult(Items.Values.FirstOrDefault(p => p.OwnerId == ownerId && p.Name == name));
        public Task<System.Collections.Generic.IReadOnlyList<Pet>> ListByOwnerAndNameAsync(string ownerId, string name, System.Threading.CancellationToken ct = default) =>
            Task.FromResult<System.Collections.Generic.IReadOnlyList<Pet>>(
                Items.Values.Where(p => p.OwnerId == ownerId && p.Name == name).ToList());
        public Task<System.Collections.Generic.IReadOnlyList<Pet>> ListByOwnerAsync(string ownerId, System.Threading.CancellationToken ct = default) =>
            Task.FromResult<System.Collections.Generic.IReadOnlyList<Pet>>(
                Items.Values.Where(p => p.OwnerId == ownerId && !p.Deleted).ToList());
        public Task<Pet> RequireAsync(string id, System.Threading.CancellationToken ct = default) =>
            FindAsync(id, ct).ContinueWith(t => t.Result!);
        public void Add(Pet entity) => Items[entity.Id] = entity;
        public void AddRange(System.Collections.Generic.IEnumerable<Pet> entities)
        {
            foreach (var e in entities) Add(e);
        }
    }

    private sealed class FakeWeightRepository : IWeightEntryRepository
    {
        public readonly List<WeightEntry> Added = [];
        public void Add(WeightEntry entry) => Added.Add(entry);
    }

    private static readonly IClock Clock = new TestClock(new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero));

    private static CommandContext Context => new(
        "T-LOCAL-DEFAULT", "U-1", "D-1", "OP-1", "COR-1", null, null, null, Clock.UtcNow);

    [Fact]
    public void CommandTypes_AreDeclaredInTheCatalog()
    {
        Assert.Contains(CommandTypes.PatientsOwnerCreate, CommandTypes.All);
        Assert.Contains(CommandTypes.PatientsOwnerUpdate, CommandTypes.All);
        Assert.Contains(CommandTypes.PatientsPetCreate, CommandTypes.All);
        Assert.Contains(CommandTypes.PatientsPetUpdate, CommandTypes.All);
        Assert.Contains(CommandTypes.PatientsPetWeightAdd, CommandTypes.All);
        Assert.Contains(CommandTypes.PatientsOwnerDelete, CommandTypes.All);
        Assert.Contains(CommandTypes.PatientsPetDelete, CommandTypes.All);
    }

    [Fact]
    public async Task CreateOwner_PersistsOwnerAndOptionalFirstPet_AndReturnsTheOwnerId()
    {
        var owners = new FakeOwnerRepository();
        var pets = new FakePetRepository();
        var handler = new CreateOwnerCommandHandler(owners, pets, Clock);

        var command = new CreateOwnerCommand("نگار محمدی", "0912 000 0001", OwnerGender.Female,
            null, null, null, "رکس", PetSpecies.Dog, "mixed", "black", PetGender.Male, null,
            null, null, false);

        var result = await handler.HandleAsync(command, Context, default);

        Assert.True(result.IsSuccess);
        Assert.True(owners.Items.ContainsKey(result.Value!));
        Assert.Single(pets.Items);
        // Journaled-command members are the catalog values.
        Assert.Equal(CommandTypes.PatientsOwnerCreate, command.CommandTypeName);
        Assert.Equal("Owner", command.AggregateTypeName);
        Assert.Null(command.AggregateIdOrNull);
    }

    [Fact]
    public async Task CreateOwnerValidator_RequiresNameAndAPhoneWithDigits()
    {
        var validator = new CreateOwnerCommandValidator();
        var invalid = new CreateOwnerCommand("", "", OwnerGender.Male, null, null, null,
            null, null, null, null, null, null, null, null, false);

        var failures = await validator.ValidateAsync(invalid, default);

        Assert.Contains(failures, f => f.Field == nameof(CreateOwnerCommand.FullName) && f.Code == "REQUIRED");
        Assert.Contains(failures, f => f.Field == nameof(CreateOwnerCommand.PhonePrimary) && f.Code == "PHONE_INVALID");
    }

    /// <summary>
    /// D10: a non-blank but digitless phone used to pass validation, normalize to the empty string and be stored —
    /// producing a server row the replica consumer refuses (PhonePrimary is REQUIRED), which dropped the whole owner
    /// and every pet/visit under it on every peer. The operator now gets a validation failure instead.
    /// </summary>
    [Theory]
    [InlineData("---")]
    [InlineData("abc")]
    [InlineData("+")]
    public async Task CreateOwnerValidator_RefusesADigitlessPhone(string raw)
    {
        var validator = new CreateOwnerCommandValidator();
        var command = new CreateOwnerCommand("سارا", raw, OwnerGender.Female, null, null, null,
            null, null, null, null, null, null, null, null, false);

        var failures = await validator.ValidateAsync(command, default);

        Assert.Contains(failures, f => f.Field == nameof(CreateOwnerCommand.PhonePrimary) && f.Code == "PHONE_INVALID");
    }

    [Fact]
    public async Task UpdateOwnerValidator_RefusesADigitlessPhone()
    {
        var failures = await new UpdateOwnerCommandValidator().ValidateAsync(
            new UpdateOwnerCommand("OWN1", "سارا", "---", OwnerGender.Female, null, null, null), default);

        Assert.Contains(failures, f => f.Field == nameof(UpdateOwnerCommand.PhonePrimary) && f.Code == "PHONE_INVALID");
    }

    [Fact]
    public async Task UpdateOwner_UnknownOwner_ReturnsOwnerNotFound()
    {
        var handler = new UpdateOwnerCommandHandler(new FakeOwnerRepository());

        var result = await handler.HandleAsync(
            new UpdateOwnerCommand("missing", "A B", "09120000000", OwnerGender.Male, null, null, null),
            Context, default);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.OwnerNotFound, result.Error);
    }

    [Fact]
    public async Task CreatePet_UnknownOwner_ReturnsOwnerNotFound()
    {
        var handler = new CreatePetCommandHandler(new FakeOwnerRepository(), new FakePetRepository(), Clock);

        var result = await handler.HandleAsync(
            new CreatePetCommand("missing", "رکس", PetSpecies.Dog, null, null, PetGender.Male, null, null, null, false),
            Context, default);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.OwnerNotFound, result.Error);
    }

    [Fact]
    public async Task AddWeightEntry_NonPositiveWeight_ReturnsWeightEntryInvalid()
    {
        var handler = new AddWeightEntryCommandHandler(new FakePetRepository(), new FakeWeightRepository(), Clock);

        var result = await handler.HandleAsync(
            new AddWeightEntryCommand("P1", 0m, Clock.UtcNow, null), Context, default);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.WeightEntryInvalid, result.Error);
    }

    [Fact]
    public async Task AddWeightEntry_UnknownPet_ReturnsPetNotFound()
    {
        var handler = new AddWeightEntryCommandHandler(new FakePetRepository(), new FakeWeightRepository(), Clock);

        var result = await handler.HandleAsync(
            new AddWeightEntryCommand("missing", 5m, Clock.UtcNow, null), Context, default);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.PetNotFound, result.Error);
    }

    [Theory]
    [InlineData(typeof(CreatePetCommand))]
    [InlineData(typeof(UpdatePetInfoCommand))]
    [InlineData(typeof(AddWeightEntryCommand))]
    [InlineData(typeof(DeletePetCommand))]
    public void PetMutations_RequireThePatientsPetEditPermission_SoALackingUserIsDenied(Type command)
    {
        // Enforcement is the [RequiresPermission] the dispatcher reads once at registration; the generic
        // deny path (subject without the code ⇒ PERMISSION_DENIED, handler skipped) is proven end-to-end in
        // AuthorizationGateTests. Here we lock the code each pet mutation demands.
        var attribute = command.GetCustomAttribute<RequiresPermissionAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal(PermissionCodes.PatientsPetEdit, attribute!.PermissionCode);
    }

    [Theory]
    [InlineData(typeof(CreateOwnerCommand))]
    [InlineData(typeof(UpdateOwnerCommand))]
    [InlineData(typeof(DeleteOwnerCommand))]
    public void OwnerMutations_RequireThePatientsOwnerEditPermission(Type command)
    {
        var attribute = command.GetCustomAttribute<RequiresPermissionAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal(PermissionCodes.PatientsOwnerEdit, attribute!.PermissionCode);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(37)]
    public async Task DirectoryService_PagesCoverEveryRowAndOwnerRelations_AfterReopeningStore(int pageSize)
    {
        using var fixture = new Animora.Persistence.Tests.LocalStoreTestFixture(
            configureOptions: options => options.MaxPageSize = pageSize,
            configureServices: services =>
            {
                services.AddScoped<IDispatcher, DirectoryDispatcher>();
                services.AddSingleton<ICurrentUserProvider, DirectoryUserProvider>();
            });
        await fixture.MigrateAsync();
        string lastOwnerId = "";
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            for (var i = 0; i < 405; i++)
            {
                // Identical names exercise the unique ID ordering tiebreaker across page boundaries.
                var owner = Owner.Register(fixture.TenantId, "مالک یکسان", $"0912{i:D7}", fixture.Clock);
                db.Add(owner);
                db.Add(Pet.Register(fixture.TenantId, owner.Id, "بیمار یکسان", PetSpecies.Dog, fixture.Clock));
                lastOwnerId = owner.Id;
            }
            for (var i = 0; i < 205; i++)
                db.Add(Pet.Register(fixture.TenantId, lastOwnerId, $"گربه {i:D3}", PetSpecies.Cat, fixture.Clock));
            await db.SaveChangesAsync();
        }

        using var reopened = fixture.BuildProvider();
        var service = new PatientsService(reopened.GetRequiredService<IServiceScopeFactory>());
        var owners = await ReadAllPagesAsync(q => service.GetOwnersPageAsync(q), pageSize);
        var pets = await ReadAllPagesAsync(q => service.GetPetsPageAsync(q), pageSize);
        Assert.Equal(405, owners.Count);
        Assert.Equal(610, pets.Count);
        Assert.Equal(405, owners.Select(o => o.Id).Distinct().Count());
        Assert.Equal(610, pets.Select(p => p.Id).Distinct().Count());
        Assert.All(pets, pet => Assert.Contains(owners, owner => owner.Id == pet.OwnerId));
        Assert.Equal(206, pets.Count(p => p.OwnerId == lastOwnerId));
        Assert.Equal(206, (await service.GetOwnerAsync(lastOwnerId))!.Pets.Count);
    }

    [Fact]
    public async Task DirectoryService_EmptyStoreReturnsEmptyLists()
    {
        using var fixture = new Animora.Persistence.Tests.LocalStoreTestFixture(configureServices: services =>
        {
            services.AddScoped<IDispatcher, DirectoryDispatcher>();
            services.AddSingleton<ICurrentUserProvider, DirectoryUserProvider>();
        });
        await fixture.MigrateAsync();
        var service = new PatientsService(fixture.Services.GetRequiredService<IServiceScopeFactory>());
        Assert.Empty((await service.GetOwnersPageAsync(FirstPage())).Items);
        Assert.Empty((await service.GetPetsPageAsync(FirstPage())).Items);
    }

    [Fact]
    public async Task DirectoryService_FailedPageSurfacesErrorCodeInsteadOfEmptyResult()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDispatcher>(new FailedDirectoryDispatcher());
        services.AddSingleton<ICurrentUserProvider, DirectoryUserProvider>();
        using var provider = services.BuildServiceProvider();
        var service = new PatientsService(provider.GetRequiredService<IServiceScopeFactory>());
        var ownersError = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetOwnersPageAsync(FirstPage(page: 2)));
        var petsError = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetPetsPageAsync(FirstPage(page: 2)));
        Assert.Equal(ErrorCodes.StoreUnavailable.Code, ownersError.Message);
        Assert.Equal(ErrorCodes.StoreUnavailable.Code, petsError.Message);
    }

    private static Animora.Contracts.Patients.PatientsDirectoryQuery FirstPage(int page = 1, int pageSize = 200) =>
        new(null, null, Animora.Contracts.Patients.VaccineFilter.All, Animora.Contracts.Patients.PatientsSortOrder.Name, page, pageSize);

    // Walks the paged API the way a caller would (no unpaged whole-directory member exists any more).
    private static async Task<List<T>> ReadAllPagesAsync<T>(
        Func<Animora.Contracts.Patients.PatientsDirectoryQuery, Task<Animora.Contracts.Patients.PatientsPage<T>>> read, int pageSize)
    {
        var items = new List<T>();
        for (var page = 1; ; page++)
        {
            var batch = await read(FirstPage(page, pageSize));
            items.AddRange(batch.Items);
            if (items.Count >= batch.TotalCount || batch.Items.Count == 0) return items;
        }
    }

    private sealed class DirectoryUserProvider : ICurrentUserProvider
    {
        public Result<QueryContext> BuildQueryContext() => Result<QueryContext>.Ok(
            new QueryContext("T-LOCAL-DEFAULT", "U-1", "COR-1"));
        public QueryContext BuildAnonymousQueryContext() => BuildQueryContext().Value!;
        public Result<CommandContext> BuildAnonymousCommandContext(string? operationId = null) => throw new NotSupportedException();
        public Task<Result<CommandContext>> BuildCommandContextAsync(string? operationId, string? dependsOn,
            long? expectedVersion, System.Threading.CancellationToken ct) => throw new NotSupportedException();
    }

    // Only the dispatcher/identity seam is substituted; paging, mapping, scopes and file storage are real.
    private sealed class DirectoryDispatcher(IPatientDirectoryQueries queries) : IDispatcher
    {
        public Task<Result<T>> SendAsync<T>(ICommand<T> command, CommandContext ctx,
            System.Threading.CancellationToken ct = default) => throw new NotSupportedException();
        public async Task<Result<T>> QueryAsync<T>(IQuery<T> query, QueryContext ctx,
            System.Threading.CancellationToken ct = default)
        {
            object? value = query switch
            {
                OwnerDirectoryQuery q => await queries.GetOwnersAsync(q.Search, q.Species, q.Sort, q.Page, q.PageSize, ct),
                PetDirectoryQuery q => await queries.GetPetsAsync(q.Search, q.Species, q.Sort, q.Page, q.PageSize, ct),
                OwnerDetailQuery q => await queries.GetOwnerAsync(q.OwnerId, ct),
                _ => throw new NotSupportedException(),
            };
            return Result<T>.Ok((T)value!);
        }
    }

    private sealed class FailedDirectoryDispatcher : IDispatcher
    {
        public Task<Result<T>> SendAsync<T>(ICommand<T> command, CommandContext ctx,
            System.Threading.CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<T>> QueryAsync<T>(IQuery<T> query, QueryContext ctx,
            System.Threading.CancellationToken ct = default)
        {
            object? first = query switch
            {
                OwnerDirectoryQuery { Page: 1 } => new PagedResult<OwnerSummaryReadModel>(
                    [new("O-1", "Owner", "09120000000", null, null, null, OwnerGender.Unspecified, Clock.UtcNow, 1)], 2, 1, 1),
                PetDirectoryQuery { Page: 1 } => new PagedResult<PetSummaryReadModel>(
                    [new("P-1", "Pet", PetSpecies.Dog, null, null, PetGender.Unknown, PetStatus.Active, "O-1", null, null, null, null, null)], 2, 1, 1),
                _ => null,
            };
            return Task.FromResult(first is null ? Result<T>.Fail(ErrorCodes.StoreUnavailable) : Result<T>.Ok((T)first));
        }
    }

    [Fact]
    public async Task UpdatePetInfo_UnknownPet_ReturnsPetNotFound()
    {
        var handler = new UpdatePetInfoCommandHandler(new FakePetRepository());

        var result = await handler.HandleAsync(
            new UpdatePetInfoCommand("missing", "رکس", null, null, PetGender.Male, null, false, null, null),
            Context, default);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.PetNotFound, result.Error);
    }

    [Fact]
    public async Task DeletePet_TombstonesThePet_AndUnknownPetReturnsPetNotFound()
    {
        var pets = new FakePetRepository();
        var pet = Pet.Register("T-LOCAL-DEFAULT", "O-1", "رکس", PetSpecies.Dog, Clock);
        pets.Add(pet);
        var handler = new DeletePetCommandHandler(pets);

        var ok = await handler.HandleAsync(new DeletePetCommand(pet.Id), Context, default);
        var missing = await handler.HandleAsync(new DeletePetCommand("missing"), Context, default);

        Assert.True(ok.IsSuccess);
        Assert.True(pet.Deleted);
        Assert.True(missing.IsFailure);
        Assert.Equal(ErrorCodes.PetNotFound, missing.Error);
    }

    [Fact]
    public async Task DeleteOwner_TombstonesOwnerAndCascadesToItsPetsOnly()
    {
        var owners = new FakeOwnerRepository();
        var pets = new FakePetRepository();
        var owner = Owner.Register("T-LOCAL-DEFAULT", "نگار محمدی", "09120000001", Clock);
        owners.Add(owner);
        var mine1 = Pet.Register("T-LOCAL-DEFAULT", owner.Id, "رکس", PetSpecies.Dog, Clock);
        var mine2 = Pet.Register("T-LOCAL-DEFAULT", owner.Id, "میشی", PetSpecies.Cat, Clock);
        var other = Pet.Register("T-LOCAL-DEFAULT", "O-OTHER", "لوکی", PetSpecies.Dog, Clock);
        pets.AddRange(new[] { mine1, mine2, other });
        var handler = new DeleteOwnerCommandHandler(owners, pets);

        var ok = await handler.HandleAsync(new DeleteOwnerCommand(owner.Id), Context, default);
        var missing = await handler.HandleAsync(new DeleteOwnerCommand("missing"), Context, default);

        Assert.True(ok.IsSuccess);
        Assert.True(owner.Deleted);
        Assert.True(mine1.Deleted);
        Assert.True(mine2.Deleted);
        Assert.False(other.Deleted);
        Assert.True(missing.IsFailure);
        Assert.Equal(ErrorCodes.OwnerNotFound, missing.Error);
    }

    [Fact]
    public void DeleteCommands_AreJournaledWithTheirCatalogTypeAndAggregate()
    {
        var pet = new DeletePetCommand("P-1");
        var owner = new DeleteOwnerCommand("O-1");

        Assert.Equal(CommandTypes.PatientsPetDelete, pet.CommandTypeName);
        Assert.Equal("Pet", pet.AggregateTypeName);
        Assert.Equal("P-1", pet.AggregateIdOrNull);
        Assert.Equal(CommandTypes.PatientsOwnerDelete, owner.CommandTypeName);
        Assert.Equal("Owner", owner.AggregateTypeName);
        Assert.Equal("O-1", owner.AggregateIdOrNull);
    }
}
