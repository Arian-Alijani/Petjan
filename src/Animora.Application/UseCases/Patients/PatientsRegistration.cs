using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.UseCases.Patients.ReadModels;

namespace Animora.Application.UseCases.Patients;

/// <summary>Registers the C01 Patients/Owners use cases (4 queries + 5 journaled commands + validators).</summary>
public static class PatientsRegistration
{
    public static ApplicationRegistrationBuilder AddPatientsUseCases(this ApplicationRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddQuery<PetDirectoryQuery, PagedResult<PetSummaryReadModel>, PetDirectoryQueryHandler>()
            .AddQuery<OwnerDirectoryQuery, PagedResult<OwnerSummaryReadModel>, OwnerDirectoryQueryHandler>()
            .AddQuery<OwnerDetailQuery, OwnerDetailReadModel?, OwnerDetailQueryHandler>()
            .AddQuery<PetDetailQuery, PetDetailReadModel?, PetDetailQueryHandler>()
            .AddCommand<CreateOwnerCommand, string, CreateOwnerCommandHandler>()
            .AddValidator<CreateOwnerCommand, CreateOwnerCommandValidator>()
            .AddCommand<UpdateOwnerCommand, Unit, UpdateOwnerCommandHandler>()
            .AddValidator<UpdateOwnerCommand, UpdateOwnerCommandValidator>()
            .AddCommand<CreatePetCommand, string, CreatePetCommandHandler>()
            .AddValidator<CreatePetCommand, CreatePetCommandValidator>()
            .AddCommand<UpdatePetInfoCommand, Unit, UpdatePetInfoCommandHandler>()
            .AddValidator<UpdatePetInfoCommand, UpdatePetInfoCommandValidator>()
            .AddCommand<AddWeightEntryCommand, Unit, AddWeightEntryCommandHandler>()
            .AddValidator<AddWeightEntryCommand, AddWeightEntryCommandValidator>();
    }
}
