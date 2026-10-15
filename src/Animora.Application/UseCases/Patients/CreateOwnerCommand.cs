using Animora.Domain.Primitives;
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

/// <summary>Creates an owner (and optionally the owner's first pet). Returns the new owner id.</summary>
[RequiresPermission(PermissionCodes.PatientsOwnerEdit)]
[Auditable(AuditCategory.Clinical, "patients.owner.create")]
public sealed record CreateOwnerCommand(
    string FullName, string PhonePrimary, OwnerGender Gender, string? PhoneSecondary, string? NationalId,
    string? Address,
    // optional first pet (mirrors NewOwnerRequest.Pet); null when no pet is created with the owner:
    string? PetName, PetSpecies? PetSpecies, string? PetBreed, string? PetColor, PetGender? PetGender,
    DateTimeOffset? PetBirthUtc, string? PetMicrochip, DateTimeOffset? PetMicrochipUtc, bool PetNeutered)
    : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.PatientsOwnerCreate;
    public string AggregateTypeName => "Owner";
    public string? AggregateIdOrNull => null;
}

public sealed class CreateOwnerCommandValidator : IValidator<CreateOwnerCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(CreateOwnerCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(c.FullName, nameof(c.FullName)) is { } name) failures.Add(name);
        else if (Rules.MaxLength(c.FullName, 120, nameof(c.FullName)) is { } tooLong) failures.Add(tooLong);
        if (Rules.Phone(c.PhonePrimary, nameof(c.PhonePrimary)) is { } phone) failures.Add(phone);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class CreateOwnerCommandHandler(IOwnerRepository owners, IPetRepository pets, IClock clock)
    : ICommandHandler<CreateOwnerCommand, string>
{
    public Task<Result<string>> HandleAsync(CreateOwnerCommand c, CommandContext context, CancellationToken ct)
    {
        var owner = Owner.Register(context.TenantId, c.FullName, c.PhonePrimary, clock, c.Gender,
            id: IdGenerator.ForOperation(context.OperationId, 0, clock.UtcNow)); // same id on client + server
        // Null email/city/notes are intentional: this is a brand-new owner row.
        owner.UpdateContactDetails(c.FullName, c.PhonePrimary, c.PhoneSecondary, c.NationalId,
            email: null, address: c.Address, city: null, gender: c.Gender, notes: null);
        owners.Add(owner);

        if (!string.IsNullOrWhiteSpace(c.PetName) && c.PetSpecies is { } species)
        {
            var pet = Pet.Register(context.TenantId, owner.Id, c.PetName, species, clock,
                c.PetGender ?? PetGender.Unknown, c.PetBirthUtc, c.PetBreed,
                id: IdGenerator.ForOperation(context.OperationId, 1, clock.UtcNow));
            pet.UpdateProfile(c.PetName, c.PetBreed, c.PetGender, c.PetBirthUtc, c.PetNeutered, c.PetColor);
            if (!string.IsNullOrWhiteSpace(c.PetMicrochip))
                pet.SetMicrochip(c.PetMicrochip, c.PetMicrochipUtc ?? clock.UtcNow);
            pets.Add(pet);
        }
        return Task.FromResult(Result<string>.Ok(owner.Id));
    }
}
