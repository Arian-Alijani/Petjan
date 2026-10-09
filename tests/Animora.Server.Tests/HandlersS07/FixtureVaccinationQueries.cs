using Animora.Application.Ports;
namespace Animora.Server.Tests.HandlersS07;
    internal sealed class FixtureVaccinationQueries(FixtureVaccinations records, FixtureHistory history) : IVaccinationQueries
    {
        public Task<bool> HasNextDoseAsync(string petId, string vaccineCode, int doseNumber, CancellationToken ct) =>
            Task.FromResult(records.Rows.Any(r => r.PetId == petId && r.VaccineCode == vaccineCode && r.DoseNumber == doseNumber));
        public Task<bool> IsMarkedDoneAsync(string recordId, CancellationToken ct) =>
            Task.FromResult(history.Rows.Any(r => r.SourceEntityId == recordId && r.Title == "Dose administered"));
        public Task<IReadOnlyList<VaccinationDueRow>> GetDueAsync(DateOnly localDay, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<VaccinationDueRow>>([]);
        public Task<VaccinationDueRow?> GetRecordAsync(string id, DateOnly localDay, CancellationToken ct) =>
            Task.FromResult<VaccinationDueRow?>(null);
        public Task<IReadOnlyList<VaccinationBoosterRow>> GetPendingBoostersAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, int take, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<VaccinationBoosterRow>>([]);
        public Task<IReadOnlyList<OwnerSmsReminderRow>> GetOwnerSmsRemindersAsync(string ownerId, int take, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<OwnerSmsReminderRow>>([]);
        public Task<IReadOnlyList<SmsTemplateReadModel>> GetTemplatesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SmsTemplateReadModel>>([]);
        public Task<(IReadOnlyList<VaccinationHistoryRow> Items, int Total)> GetHistoryAsync(
            DateTimeOffset? fromUtc, DateTimeOffset? toUtc, string? search, bool oldest,
            IReadOnlyCollection<string>? titles, int page, int pageSize, CancellationToken ct) =>
            Task.FromResult<(IReadOnlyList<VaccinationHistoryRow>, int)>(([], 0));
    }