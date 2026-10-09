using Animora.Application.Dispatch;
using Animora.Contracts.Wire.Commands;
using Animora.Server.Commands;
using Animora.Application.UseCases.Patients;
using Animora.Application.UseCases.Reception;
using Animora.Application.UseCases.Visits;
using Animora.Application.UseCases.Vaccination;

namespace Animora.Server.Handlers;

internal static class ServerHandlerModuleS07
{
    public static void AddHandlers(ApplicationRegistrationBuilder builder) =>
        builder.AddPatientsUseCases().AddReceptionUseCases().AddVisitsUseCases().AddVaccinationUseCases();

    public static void AddPayloads(CommandPayloadCatalogBuilder catalog)
    {
        catalog.Add<CreateOwnerCommand>(CommandTypes.PatientsOwnerCreate, "Owner");
        catalog.Add<DeleteOwnerCommand>(CommandTypes.PatientsOwnerDelete, "Owner");
        catalog.Add<UpdateOwnerCommand>(CommandTypes.PatientsOwnerUpdate, "Owner");
        catalog.Add<CreatePetCommand>(CommandTypes.PatientsPetCreate, "Pet");
        catalog.Add<DeletePetCommand>(CommandTypes.PatientsPetDelete, "Pet");
        catalog.Add<UpdatePetInfoCommand>(CommandTypes.PatientsPetUpdate, "Pet");
        catalog.Add<AddWeightEntryCommand>(CommandTypes.PatientsPetWeightAdd, "Pet");
        catalog.Add<AdmitTicketCommand>(CommandTypes.ReceptionTicketAdmit, "ReceptionQueueEntry");
        catalog.Add<EnterVisitCommand>(CommandTypes.ReceptionTicketEnter, "ReceptionQueueEntry");
        catalog.Add<LeaveQueueCommand>(CommandTypes.ReceptionTicketLeave, "ReceptionQueueEntry");
        catalog.Add<MarkNoShowCommand>(CommandTypes.ReceptionTicketNoShow, "Appointment");
        catalog.Add<ToggleTicketUrgentCommand>(CommandTypes.ReceptionTicketUrgent, "ReceptionQueueEntry");
        catalog.Add<CreateWalkInCommand>(CommandTypes.ReceptionWalkInCreate, "ReceptionQueueEntry");
        catalog.Add<MarkVaccinationDoneCommand>(CommandTypes.VaccinationRecordAdminister, "VaccinationRecord");
        catalog.Add<RecordVaccinationReminderCommand>(CommandTypes.VaccinationRecordReminder, "VaccinationRecord");
        catalog.Add<AddPrescriptionLineCommand>(CommandTypes.VisitsRxAdd, "Visit");
        catalog.Add<RemovePrescriptionLineCommand>(CommandTypes.VisitsRxRemove, "Visit");
        catalog.Add<CompleteVisitCommand>(CommandTypes.VisitsVisitComplete, "Visit");
        catalog.Add<SaveVisitNoteCommand>(CommandTypes.VisitsVisitNote, "Visit");
        catalog.Add<StartVisitFromTicketCommand>(CommandTypes.VisitsVisitStart, "Visit");
        catalog.Add<StartNewVisitCommand>(CommandTypes.VisitsVisitStart, "Visit");
        catalog.Add<SaveVisitVitalsCommand>(CommandTypes.VisitsVisitVitals, "Visit");
        catalog.Add<CreateTreatmentPlanCommand>(CommandTypes.VaccinationTreatmentCreate, "TreatmentPlan");
        catalog.Add<UpdateTreatmentPlanCommand>(CommandTypes.VaccinationTreatmentUpdate, "TreatmentPlan");
        catalog.Add<CancelTreatmentPlanCommand>(CommandTypes.VaccinationTreatmentCancel, "TreatmentPlan");
        catalog.Add<MarkTreatmentOccurrenceDoneCommand>(CommandTypes.VaccinationTreatmentOccurrenceDone, "TreatmentOccurrence");
        catalog.Add<AddVaccineCatalogItemCommand>(CommandTypes.VaccinationCatalogAdd, "VaccineCatalogItem");
    }
}
