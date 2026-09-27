using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Patients;

/// <summary>Creates a pet under an existing owner; returns the new pet id.</summary>
[RequiresPermission(PermissionCodes.PatientsPetEdit)]
[Auditable(AuditCategory.Clinical, "patients.pet.create")]
public sealed record CreatePetCommand(string OwnerId, string Name, PetSpecies Species, string? Breed,
    string? Color, PetGender Gender, DateTimeOffset? BirthUtc, string? Microchip, DateTimeOffset? MicrochipUtc,
    bool Neutered) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.PatientsPetCreate;
    public string AggregateTypeName => "Pet";
    public string? AggregateIdOrNull => null;
}

public sealed class CreatePetCommandValidator : IValidator<CreatePetCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(CreatePetCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(c.OwnerId, nameof(c.OwnerId)) is { } owner) failures.Add(owner);
        if (Rules.Required(c.Name, nameof(c.Name)) is { } name) failures.Add(name);
        else if (Rules.MaxLength(c.Name, 120, nameof(c.Name)) is { } tooLong) failures.Add(tooLong);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class CreatePetCommandHandler(IOwnerRepository owners, IPetRepository pets, IClock clock)
    : ICommandHandler<CreatePetCommand, string>
{
    public async Task<Result<string>> HandleAsync(CreatePetCommand c, CommandContext context, CancellationToken ct)
    {
        var owner = await owners.FindAsync(c.OwnerId, ct);
        if (owner is null) return Result<string>.Fail(ErrorCodes.OwnerNotFound);
        var pet = Pet.Register(context.TenantId, c.OwnerId, c.Name, c.Species, clock, c.Gender, c.BirthUtc, c.Breed);
        pet.UpdateProfile(c.Name, c.Breed, c.Gender, c.BirthUtc, c.Neutered, c.Color);
        if (!string.IsNullOrWhiteSpace(c.Microchip)) pet.SetMicrochip(c.Microchip, c.MicrochipUtc ?? clock.UtcNow);
        pets.Add(pet);
        return Result<string>.Ok(pet.Id);
    }
}
