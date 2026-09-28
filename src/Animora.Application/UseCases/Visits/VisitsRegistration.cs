using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.UseCases.Visits.ReadModels;

namespace Animora.Application.UseCases.Visits;

public static class VisitsRegistration
{
    public static ApplicationRegistrationBuilder AddVisitsUseCases(this ApplicationRegistrationBuilder builder) => builder
        .AddQuery<TriageDoctorRailQuery, IReadOnlyList<DoctorRailReadModel>, TriageDoctorRailQueryHandler>()
        .AddQuery<BookedForDoctorQuery, IReadOnlyList<BookedAppointmentReadModel>, BookedForDoctorQueryHandler>()
        .AddQuery<TriagePatientsQuery, IReadOnlyList<TriagePatientReadModel>, TriagePatientsQueryHandler>()
        .AddCommand<StartVisitFromTicketCommand, EncounterDraftReadModel, StartVisitFromTicketCommandHandler>()
        .AddValidator<StartVisitFromTicketCommand, StartVisitFromTicketCommandValidator>()
        .AddCommand<StartNewVisitCommand, EncounterDraftReadModel, StartNewVisitCommandHandler>()
        .AddValidator<StartNewVisitCommand, StartNewVisitCommandValidator>()
        .AddQuery<EncounterDraftQuery, EncounterEditReadModel, EncounterDraftQueryHandler>()
        .AddQuery<RxCatalogQuery, IReadOnlyList<RxCatalogItemReadModel>, RxCatalogQueryHandler>()
        .AddCommand<SaveVisitVitalsCommand, Unit, SaveVisitVitalsCommandHandler>()
        .AddValidator<SaveVisitVitalsCommand, SaveVisitVitalsCommandValidator>()
        .AddCommand<SaveVisitNoteCommand, Unit, SaveVisitNoteCommandHandler>()
        .AddValidator<SaveVisitNoteCommand, SaveVisitNoteCommandValidator>()
        .AddCommand<AddPrescriptionLineCommand, string, AddPrescriptionLineCommandHandler>()
        .AddValidator<AddPrescriptionLineCommand, AddPrescriptionLineCommandValidator>()
        .AddCommand<RemovePrescriptionLineCommand, Unit, RemovePrescriptionLineCommandHandler>()
        .AddValidator<RemovePrescriptionLineCommand, RemovePrescriptionLineCommandValidator>()
        .AddCommand<CompleteVisitCommand, EncounterSummaryReadModel, CompleteVisitCommandHandler>()
        .AddValidator<CompleteVisitCommand, CompleteVisitCommandValidator>();
}
