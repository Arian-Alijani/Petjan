using Animora.Application.Dispatch;
using Animora.Application.Ports;

namespace Animora.Application.UseCases.Vaccination;

public static class VaccinationRegistration
{
    public static ApplicationRegistrationBuilder AddVaccinationUseCases(this ApplicationRegistrationBuilder builder) => builder
        .AddQuery<VaccinationDueQuery, IReadOnlyList<VaccinationDueRow>, VaccinationDueQueryHandler>()
        .AddQuery<VaccinationCatalogQuery, IReadOnlyList<VaccineCatalogReadModel>, VaccinationCatalogQueryHandler>()
        .AddQuery<SmsTemplatesQuery, IReadOnlyList<SmsTemplateReadModel>, SmsTemplatesQueryHandler>()
        .AddQuery<OwnerSmsRemindersQuery, IReadOnlyList<OwnerSmsReminderRow>, OwnerSmsRemindersQueryHandler>()
        .AddQuery<VaccinationHistoryQuery, VaccinationHistoryPageReadModel, VaccinationHistoryQueryHandler>()
        .AddQuery<TreatmentCalendarQuery, IReadOnlyList<TreatmentCalendarRow>, TreatmentCalendarQueryHandler>()
        .AddQuery<TreatmentPlansForPetQuery, IReadOnlyList<TreatmentPlanRow>, TreatmentPlansForPetQueryHandler>()
        .AddCommand<CreateTreatmentPlanCommand, string, CreateTreatmentPlanCommandHandler>()
        .AddValidator<CreateTreatmentPlanCommand, CreateTreatmentPlanCommandValidator>()
        .AddCommand<UpdateTreatmentPlanCommand, string, UpdateTreatmentPlanCommandHandler>()
        .AddValidator<UpdateTreatmentPlanCommand, UpdateTreatmentPlanCommandValidator>()
        .AddCommand<CancelTreatmentPlanCommand, string, CancelTreatmentPlanCommandHandler>()
        .AddValidator<CancelTreatmentPlanCommand, CancelTreatmentPlanCommandValidator>()
        .AddCommand<MarkTreatmentOccurrenceDoneCommand, string, MarkTreatmentOccurrenceDoneCommandHandler>()
        .AddValidator<MarkTreatmentOccurrenceDoneCommand, MarkTreatmentOccurrenceDoneCommandValidator>()
        .AddCommand<AddVaccineCatalogItemCommand, string, AddVaccineCatalogItemCommandHandler>()
        .AddValidator<AddVaccineCatalogItemCommand, AddVaccineCatalogItemCommandValidator>()
        .AddCommand<MarkVaccinationDoneCommand, string, MarkVaccinationDoneCommandHandler>()
        .AddValidator<MarkVaccinationDoneCommand, MarkVaccinationDoneCommandValidator>()
        .AddCommand<RegisterVaccinationCommand, string, RegisterVaccinationCommandHandler>()
        .AddValidator<RegisterVaccinationCommand, RegisterVaccinationCommandValidator>()
        .AddCommand<RecordVaccinationReminderCommand, string, RecordVaccinationReminderCommandHandler>()
        .AddValidator<RecordVaccinationReminderCommand, RecordVaccinationReminderCommandValidator>();
}
