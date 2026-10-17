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
        .AddCommand<AddRadiologyCommand, string, AddRadiologyCommandHandler>()
        .AddValidator<AddRadiologyCommand, AddRadiologyCommandValidator>()
        .AddCommand<RemoveRadiologyCommand, Unit, RemoveRadiologyCommandHandler>()
        .AddValidator<RemoveRadiologyCommand, RemoveRadiologyCommandValidator>()
        .AddCommand<AddLabResultCommand, string, AddLabResultCommandHandler>()
        .AddValidator<AddLabResultCommand, AddLabResultCommandValidator>()
        .AddCommand<RemoveLabResultCommand, Unit, RemoveLabResultCommandHandler>()
        .AddValidator<RemoveLabResultCommand, RemoveLabResultCommandValidator>()
        .AddCommand<CompleteVisitCommand, EncounterSummaryReadModel, CompleteVisitCommandHandler>()
        .AddValidator<CompleteVisitCommand, CompleteVisitCommandValidator>();

    /// <summary>
    /// Desktop-only visits use-cases. <see cref="RefreshVisitDoctorDirectoryCommand"/> materializes the
    /// server doctor roster into the LOCAL clinical store (it is never journalled back to the server), so its
    /// handler depends on <c>IVisitDoctorDirectoryWriter</c> which only the SQLite/local persistence layer
    /// registers. Registering it server-side made <c>ValidateOnBuild</c> fail ("Unable to resolve service for
    /// type 'IVisitDoctorDirectoryWriter'") and the server crashed on startup, so it lives here, called only by
    /// the desktop host.
    /// </summary>
    public static ApplicationRegistrationBuilder AddVisitsLocalUseCases(this ApplicationRegistrationBuilder builder) => builder
        .AddCommand<RefreshVisitDoctorDirectoryCommand, Unit, RefreshVisitDoctorDirectoryCommandHandler>();
}
