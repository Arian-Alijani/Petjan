using System;
using System.Threading.Tasks;
using System.Reflection;
using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Patients;
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
    public async Task CreateOwnerValidator_RequiresNameAndPrimaryPhone()
    {
        var validator = new CreateOwnerCommandValidator();
        var invalid = new CreateOwnerCommand("", "", OwnerGender.Male, null, null, null,
            null, null, null, null, null, null, null, null, false);

        var failures = await validator.ValidateAsync(invalid, default);

        Assert.Contains(failures, f => f.Field == nameof(CreateOwnerCommand.FullName) && f.Code == "REQUIRED");
        Assert.Contains(failures, f => f.Field == nameof(CreateOwnerCommand.PhonePrimary) && f.Code == "REQUIRED");
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
    public void OwnerMutations_RequireThePatientsOwnerEditPermission(Type command)
    {
        var attribute = command.GetCustomAttribute<RequiresPermissionAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal(PermissionCodes.PatientsOwnerEdit, attribute!.PermissionCode);
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
}
