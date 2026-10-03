using Animora.Application.Dispatch;
using Animora.Application.Ports;

namespace Animora.Application.UseCases.Vaccination;

public static class VaccinationRegistration
{
    public static ApplicationRegistrationBuilder AddVaccinationUseCases(this ApplicationRegistrationBuilder builder) => builder
        .AddQuery<VaccinationDueQuery, IReadOnlyList<VaccinationDueRow>, VaccinationDueQueryHandler>()
        .AddQuery<VaccinationCatalogQuery, IReadOnlyList<VaccineCatalogReadModel>, VaccinationCatalogQueryHandler>()
        .AddQuery<SmsTemplatesQuery, IReadOnlyList<SmsTemplateReadModel>, SmsTemplatesQueryHandler>()
        .AddQuery<VaccinationBoostersRangeQuery, IReadOnlyList<VaccinationBoosterRow>, VaccinationBoostersRangeQueryHandler>()
        .AddQuery<OwnerSmsRemindersQuery, IReadOnlyList<OwnerSmsReminderRow>, OwnerSmsRemindersQueryHandler>()
        .AddQuery<VaccinationHistoryQuery, VaccinationHistoryPageReadModel, VaccinationHistoryQueryHandler>()
        .AddCommand<MarkVaccinationDoneCommand, string, MarkVaccinationDoneCommandHandler>()
        .AddValidator<MarkVaccinationDoneCommand, MarkVaccinationDoneCommandValidator>()
        .AddCommand<RecordVaccinationReminderCommand, string, RecordVaccinationReminderCommandHandler>()
        .AddValidator<RecordVaccinationReminderCommand, RecordVaccinationReminderCommandValidator>();
}
