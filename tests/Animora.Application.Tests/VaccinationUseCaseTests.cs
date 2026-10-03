using Animora.Application.Ports;
using Animora.Application.UseCases.Vaccination;
using Animora.Domain.Clinical;
using Animora.Domain.Results;
using Xunit;
using static Animora.Application.Tests.ReceptionTestDoubles;

namespace Animora.Application.Tests;

public sealed class VaccinationUseCaseTests
{
    [Theory]
    [InlineData(-1, VaccinationDueBucket.Overdue)]
    [InlineData(0, VaccinationDueBucket.DueSoon)]
    [InlineData(14, VaccinationDueBucket.DueSoon)]
    [InlineData(15, VaccinationDueBucket.Upcoming)]
    [InlineData(60, VaccinationDueBucket.Upcoming)]
    [InlineData(61, VaccinationDueBucket.Scheduled)]
    public void DueThresholds_RespectEveryBoundary(int days, VaccinationDueBucket expected) =>
        Assert.Equal(expected, VaccinationThresholds.BucketFor(days, false));

    [Fact]
    public async Task AdministerFollowup_CreatesExactlyOneNextDose_AndSecondAttemptRejected()
    {
        var records = new RecordingVaccinations();
        var history = new RecordingHistory();
        var first = VaccinationRecord.Administer(TenantId, "PET-1", "DHPP", "DHPP",
            1, Clock.UtcNow.AddDays(-21), "U-1", Clock, totalDoses: 3).Value!;
        records.Add(first);
        var handler = new MarkVaccinationDoneCommandHandler(records, new VaccinationLookup(records, history), history, Clock);

        var initial = await handler.HandleAsync(new MarkVaccinationDoneCommand(first.Id), Context, default);
        var repeated = await handler.HandleAsync(new MarkVaccinationDoneCommand(first.Id), Context, default);

        Assert.True(initial.IsSuccess);
        Assert.Equal(2, records.Rows.Count);
        Assert.Equal(2, records.Rows.Single(r => r.Id != first.Id).DoseNumber);
        Assert.Single(history.Rows);
        Assert.Equal(ErrorCodes.VaccinationAlreadyDone, repeated.Error);
    }

    private sealed class RecordingVaccinations : IVaccinationRepository
    {
        public List<VaccinationRecord> Rows { get; } = [];
        public Task<VaccinationRecord?> FindAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(Rows.FirstOrDefault(r => r.Id == id));
        public async Task<VaccinationRecord> RequireAsync(string id, CancellationToken ct = default) =>
            await FindAsync(id, ct) ?? throw new InvalidOperationException();
        public void Add(VaccinationRecord entity) => Rows.Add(entity);
        public void AddRange(IEnumerable<VaccinationRecord> entities) => Rows.AddRange(entities);
    }

    private sealed class RecordingHistory : IMedicalHistoryRepository
    {
        public List<MedicalHistoryEntry> Rows { get; } = [];
        public void Add(MedicalHistoryEntry entry) => Rows.Add(entry);
    }

    private sealed class VaccinationLookup(RecordingVaccinations records, RecordingHistory history) : IVaccinationQueries
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
}
