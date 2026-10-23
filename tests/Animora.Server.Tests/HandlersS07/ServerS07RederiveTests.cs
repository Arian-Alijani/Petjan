using Animora.Application.UseCases.Patients;
using Animora.Application.UseCases.Vaccination;
using Animora.Application.UseCases.Visits;
using Animora.Domain.Clinical;
using Animora.Domain.Patients;
using Animora.Domain.Results;
using Xunit;

namespace Animora.Server.Tests.HandlersS07;

public sealed class ServerS07RederiveTests
{
    [Theory, InlineData(0), InlineData(-1), InlineData(301)]
    public void WeightBounds_AreDomainDerived(decimal kg)
    {
        using var f = new ClinicalHandlerFixture();
        var result = WeightEntry.Record(f.Intake.TenantId, f.Pet.Id, kg, f.Intake.Clock.UtcNow, f.DoctorId, f.Intake.Clock);
        Assert.Equal(ErrorCodes.WeightOutOfRange, result.Error);
    }
    [Fact]
    public void PetSpecies_CannotBeChangedByTheUpdatePayload()
    {
        Assert.Null(typeof(UpdatePetInfoCommand).GetProperty("Species"));
        Assert.False(typeof(Pet).GetProperty(nameof(Pet.Species))!.SetMethod!.IsPublic);
    }
    [Fact]
    public async Task OwnerDeletion_CurrentSharedHandlerCascadesWithoutOrphans()
    {
        // Live C01 behavior supersedes the old OWNER_HAS_ACTIVE_PETS recipe: retain client/server parity.
        using var f = new ClinicalHandlerFixture();
        var result = await new DeleteOwnerCommandHandler(f.Owners, f.Pets, f.Intake.Clock)
            .HandleAsync(new DeleteOwnerCommand(f.Owner.Id), ReceptionTestDoubles.Context, default);
        Assert.True(result.IsSuccess); Assert.True(f.Owner.Deleted); Assert.True(f.Pet.Deleted);
    }
    [Fact]
    public void Visit_RequiresDiagnosis_AndInvoiceLocksLines()
    {
        using var f = new ClinicalHandlerFixture();
        f.Visit.SetAssessment(null);
        Assert.Equal(ErrorCodes.VisitDiagnosisRequired, f.Visit.Complete(f.Intake.Clock).Error);
        f.Visit.SetAssessment("Healthy"); Assert.True(f.Visit.Complete(f.Intake.Clock).IsSuccess);
        Assert.True(f.Visit.MarkReadyForInvoice().IsSuccess); Assert.True(f.Visit.AttachInvoice("invoice").IsSuccess);
        Assert.Equal(ErrorCodes.VisitLocked, f.Visit.RemoveServiceLine(f.LineId).Error);
    }
    [Fact]
    public void TreatmentSchedule_IsBounded_NotTruncated()
    {
        var today = new DateOnly(2026, 10, 9);
        Assert.Equal(ErrorCodes.TreatmentScheduleInvalid, TreatmentScheduleExpander.Expand(TreatmentSchedule.Every(today, 0, count: 2), today).Error);
        Assert.Equal(ErrorCodes.TreatmentScheduleTooLarge, TreatmentScheduleExpander.Expand(TreatmentSchedule.Every(today, 1, count: 366), today).Error);
    }
    [Fact]
    public async Task Occurrence_IsIdempotent_AndCatalogCodeIsUnique()
    {
        using var f = new ClinicalHandlerFixture();
        var context = new Animora.Application.Abstractions.CommandContext(f.Intake.TenantId, f.Intake.UserId, f.Intake.DeviceId,
            "op", "cor", null, null, null, f.Intake.Clock.UtcNow);
        var done = new MarkTreatmentOccurrenceDoneCommandHandler(f.Plans, f.History, f.Intake.Clock);
        var command = new MarkTreatmentOccurrenceDoneCommand(f.Plan.Occurrences[0].Id);
        Assert.True((await done.HandleAsync(command, context, default)).IsSuccess);
        Assert.Equal(ErrorCodes.TreatmentOccurrenceAlreadyDone, (await done.HandleAsync(command, context, default)).Error);
        var add = new AddVaccineCatalogItemCommandHandler(f.Plans, f.Intake.Clock);
        var catalog = new AddVaccineCatalogItemCommand("custom", "Custom");
        Assert.True((await add.HandleAsync(catalog, context, default)).IsSuccess);
        Assert.Equal(ErrorCodes.VaccineCatalogCodeTaken, (await add.HandleAsync(catalog, context, default)).Error);
    }
}
