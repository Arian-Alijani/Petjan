using Animora.Application.Ports;
using Animora.Application.UseCases.Visits;
using Animora.Application.UseCases.Visits.ReadModels;
using Animora.Domain.Clinical;
using Animora.Domain.Results;
using Xunit;
using static Animora.Server.Tests.HandlersS07.ReceptionTestDoubles;

namespace Animora.Server.Tests.HandlersS07;

public sealed class VisitEncounterUseCaseTests
{
    private readonly FakeVisits _visits = new();
    private readonly FakePets _pets = new();
    private readonly FakeQueueRepository _queue = new();
    private readonly RecordingHistory _history = new();
    private readonly RecordingHandoff _handoff = new();

    private Visit OpenVisit()
    {
        var visit = Visit.Open(TenantId, "PET-1", "OWNER-1", "U-1", VisitReason.Checkup, Clock);
        Assert.True(visit.Start().IsSuccess);
        _visits.Add(visit);
        return visit;
    }

    private CompleteVisitCommandHandler CompleteHandler() =>
        new(_visits, _history, _handoff, _queue, _pets, new EmptyTriage(), new FakeAppointmentRepository(), Clock);

    [Fact]
    public async Task EditingNoteOnCompletedVisit_IsRejected()
    {
        var visit = OpenVisit();
        visit.SetAssessment("Healthy", null, null);
        visit.Complete(Clock);

        var result = await new SaveVisitNoteCommandHandler(_visits).HandleAsync(
            new SaveVisitNoteCommand(visit.Id, VisitNoteField.Exam, "Exam"), Context, default);

        Assert.Equal(ErrorCodes.VisitNotEditable, result.Error);
    }

    [Fact]
    public async Task CompletingWithoutDiagnosisOrLines_IsIncomplete()
    {
        var visit = OpenVisit();
        var result = await CompleteHandler().HandleAsync(new CompleteVisitCommand(visit.Id), Context, default);
        Assert.Equal(ErrorCodes.VisitIncomplete, result.Error);
        Assert.Empty(_history.Rows);
        Assert.Empty(_handoff.VisitIds);
    }

    [Fact]
    public async Task CompletingOnce_WritesOneHistoryAndHandoff_SecondAttemptRejected()
    {
        var visit = OpenVisit();
        visit.SetAssessment("Healthy", "Follow up", null);
        var handler = CompleteHandler();

        var first = await handler.HandleAsync(new CompleteVisitCommand(visit.Id), Context, default);
        var second = await handler.HandleAsync(new CompleteVisitCommand(visit.Id), Context, default);

        Assert.True(first.IsSuccess);
        Assert.Equal(VisitState.ReadyForInvoice, visit.State);
        Assert.Equal(ErrorCodes.VisitNotEditable, second.Error);
        Assert.Equal(visit.Id, Assert.Single(_handoff.VisitIds));
        Assert.Equal(visit.Id, Assert.Single(_history.Rows).SourceEntityId);
    }

    private sealed class RecordingHistory : IMedicalHistoryRepository
    {
        public List<MedicalHistoryEntry> Rows { get; } = [];
        public void Add(MedicalHistoryEntry entry) => Rows.Add(entry);
    }

    private sealed class RecordingHandoff : IVisitBillingHandoffWriter
    {
        public List<string> VisitIds { get; } = [];
        public void Add(string id, string tenantId, string visitId, string ownerId, string petId, string doctorId,
            DateTimeOffset completedUtc, string linesJson) => VisitIds.Add(visitId);
    }

    private sealed class EmptyTriage : ITriageQueries
    {
        public Task<IReadOnlyList<DoctorRailReadModel>> GetDoctorRailAsync(
            DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DoctorRailReadModel>>([]);

        public Task<IReadOnlyList<BookedAppointmentReadModel>> GetBookedForDoctorAsync(
            string doctorId, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BookedAppointmentReadModel>>([]);

        public Task<IReadOnlyList<TriagePatientReadModel>> GetRecentPatientsAsync(string? search, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TriagePatientReadModel>>([]);
    }
}
