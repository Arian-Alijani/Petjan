using Animora.Application.UseCases.Patients;
using Animora.Application.UseCases.Reception;
using Animora.Application.UseCases.Visits;
using Animora.Application.UseCases.Vaccination;
using Animora.Contracts.Wire.Commands;
using Xunit;
using Animora.Domain.Results;

namespace Animora.Server.Tests.HandlersS07;

public sealed class ClinicalIntakeTests
{
    [Fact] public Task Settles_CreateOwnerCommand() => SettlesAsync(typeof(CreateOwnerCommand));
    [Fact] public Task Settles_DeleteOwnerCommand() => SettlesAsync(typeof(DeleteOwnerCommand));
    [Fact] public Task Settles_UpdateOwnerCommand() => SettlesAsync(typeof(UpdateOwnerCommand));
    [Fact] public Task Settles_CreatePetCommand() => SettlesAsync(typeof(CreatePetCommand));
    [Fact] public Task Settles_DeletePetCommand() => SettlesAsync(typeof(DeletePetCommand));
    [Fact] public Task Settles_UpdatePetInfoCommand() => SettlesAsync(typeof(UpdatePetInfoCommand));
    [Fact] public Task Settles_AddWeightEntryCommand() => SettlesAsync(typeof(AddWeightEntryCommand));
    [Fact] public Task Settles_AdmitTicketCommand() => SettlesAsync(typeof(AdmitTicketCommand));
    [Fact] public Task Settles_EnterVisitCommand() => SettlesAsync(typeof(EnterVisitCommand));
    [Fact] public Task Settles_LeaveQueueCommand() => SettlesAsync(typeof(LeaveQueueCommand));
    [Fact] public Task Settles_MarkNoShowCommand() => SettlesAsync(typeof(MarkNoShowCommand));
    [Fact] public Task Settles_ToggleTicketUrgentCommand() => SettlesAsync(typeof(ToggleTicketUrgentCommand));
    [Fact] public Task Settles_CreateWalkInCommand() => SettlesAsync(typeof(CreateWalkInCommand));
    [Fact] public Task Settles_MarkVaccinationDoneCommand() => SettlesAsync(typeof(MarkVaccinationDoneCommand));
    [Fact] public Task Settles_RecordVaccinationReminderCommand() => SettlesAsync(typeof(RecordVaccinationReminderCommand));
    [Fact] public Task Settles_AddPrescriptionLineCommand() => SettlesAsync(typeof(AddPrescriptionLineCommand));
    [Fact] public Task Settles_RemovePrescriptionLineCommand() => SettlesAsync(typeof(RemovePrescriptionLineCommand));
    [Fact] public Task Settles_CompleteVisitCommand() => SettlesAsync(typeof(CompleteVisitCommand));
    [Fact] public Task Settles_SaveVisitNoteCommand() => SettlesAsync(typeof(SaveVisitNoteCommand));
    [Fact] public Task Settles_StartVisitFromTicketCommand() => SettlesAsync(typeof(StartVisitFromTicketCommand));
    [Fact] public Task Settles_StartNewVisitCommand() => SettlesAsync(typeof(StartNewVisitCommand));
    [Fact] public Task Settles_SaveVisitVitalsCommand() => SettlesAsync(typeof(SaveVisitVitalsCommand));
    [Fact] public Task Settles_CreateTreatmentPlanCommand() => SettlesAsync(typeof(CreateTreatmentPlanCommand));
    [Fact] public Task Settles_UpdateTreatmentPlanCommand() => SettlesAsync(typeof(UpdateTreatmentPlanCommand));
    [Fact] public Task Settles_CancelTreatmentPlanCommand() => SettlesAsync(typeof(CancelTreatmentPlanCommand));
    [Fact] public Task Settles_MarkTreatmentOccurrenceDoneCommand() => SettlesAsync(typeof(MarkTreatmentOccurrenceDoneCommand));
    [Fact] public Task Settles_AddVaccineCatalogItemCommand() => SettlesAsync(typeof(AddVaccineCatalogItemCommand));

    private static async Task SettlesAsync(Type record)
    {
        using var valid = new ClinicalHandlerFixture();
        var command = valid.Command(record, false);
        var service = valid.Intake.CreateService(valid.Catalog, valid.Dispatcher);
        var envelope = valid.Envelope(command);
        var accepted = await service.SubmitAsync(envelope);
        Assert.True(accepted.IsSuccess);
        Assert.Equal(SettlementOutcome.Accepted, accepted.Value!.Outcome);
        var replay = await service.SubmitAsync(envelope);
        Assert.Equal(accepted.Value, replay.Value);
        Assert.Equal(1, valid.Dispatcher.Calls);
        Assert.Equal(FinancialEffect.None, accepted.Value.FinancialEffect);

        using var invalid = new ClinicalHandlerFixture();
        var rejected = await invalid.Intake.CreateService(invalid.Catalog, invalid.Dispatcher)
            .SubmitAsync(invalid.Envelope(invalid.Command(record, true)));
        Assert.True(rejected.IsSuccess);
        Assert.Equal(SettlementOutcome.Rejected, rejected.Value!.Outcome);
        var expected = record.Name switch
        {
            nameof(DeleteOwnerCommand) => ErrorCodes.OwnerNotFound,
            nameof(DeletePetCommand) or nameof(AddWeightEntryCommand) => ErrorCodes.PetNotFound,
            nameof(CreateWalkInCommand) => ErrorCodes.WalkInPhoneRequired,
            nameof(AdmitTicketCommand) or nameof(EnterVisitCommand) or nameof(LeaveQueueCommand)
                or nameof(MarkNoShowCommand) or nameof(ToggleTicketUrgentCommand) or nameof(StartVisitFromTicketCommand) => ErrorCodes.TicketNotFound,
            nameof(SaveVisitVitalsCommand) or nameof(SaveVisitNoteCommand) or nameof(RemovePrescriptionLineCommand)
                or nameof(CompleteVisitCommand) => ErrorCodes.VisitNotFound,
            nameof(CancelTreatmentPlanCommand) => ErrorCodes.TreatmentPlanNotFound,
            nameof(MarkTreatmentOccurrenceDoneCommand) => ErrorCodes.TreatmentOccurrenceNotFound,
            nameof(MarkVaccinationDoneCommand) or nameof(RecordVaccinationReminderCommand) => ErrorCodes.VaccinationNotFound,
            _ => ErrorCodes.ValidationFailed
        };
        Assert.Equal(expected.Code, rejected.Value.ErrorCode);
        Assert.NotEqual("HANDLER_NOT_REGISTERED", rejected.Value.ErrorCode);
    }
}
