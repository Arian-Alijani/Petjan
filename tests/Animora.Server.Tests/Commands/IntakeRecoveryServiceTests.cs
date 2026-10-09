using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Commands;
using Animora.Server.Commands;
using Xunit;

namespace Animora.Server.Tests.Commands;

public sealed class IntakeRecoveryServiceTests
{
    [Fact]
    public async Task ExpiredLease_NotBlindlyRetried_FencedByAttemptId()
    {
        var f = new IntakeTestFixture();
        var row = f.Row(f.Envelope(), expired: true);
        f.Store.Rows.Add(row.OperationId, row);
        string? previous = row.AttemptId;
        var probe = new CountingProbe();
        var service = new IntakeRecoveryService(f.Store, [new NoProbe(), probe], f.Clock);
        f.Store.LoseFence = true;
        var fenced = await service.RecoverAsync(row);
        Assert.True(fenced.IsFailure);
        Assert.Equal(previous, f.Store.ObservedAttempt);
        Assert.Equal(0, probe.Calls);
        f.Store.LoseFence = false;
        var recovered = await service.RecoverAsync(row);
        Assert.True(recovered.IsSuccess);
        Assert.NotEqual(previous, recovered.Value);
        Assert.Equal(recovered.Value, f.Store.AcquiredAttempt);
        Assert.Equal(1, probe.Calls);
        Assert.Equal(0, f.Dispatcher.Calls);
    }

    [Fact]
    public async Task ThreeExpiredLeases_MarksFailed()
    {
        var f = new IntakeTestFixture();
        var row = f.Row(f.Envelope(), expired: true);
        FakeIntakeStore.Set(row, nameof(row.AttemptCount), (short)3);
        f.Store.Rows.Add(row.OperationId, row);
        string? old = row.AttemptId;
        var result = await new IntakeRecoveryService(f.Store, [new NoProbe()], f.Clock).RecoverAsync(row);
        Assert.True(result.IsFailure);
        Assert.Equal(IntakeStatus.Failed, row.Status);
        Assert.NotEqual(old, f.Store.FailedAttempt);
        Assert.Equal(f.Store.AcquiredAttempt, f.Store.FailedAttempt);
    }

    [Fact]
    public async Task CommittedEvidence_RequiresReconciliation_NeverReruns()
    {
        var f = new IntakeTestFixture();
        var row = f.Row(f.Envelope(), expired: true);
        f.Store.Rows.Add(row.OperationId, row);
        var probe = new CountingProbe { Committed = true };
        var result = await new IntakeRecoveryService(f.Store, [probe], f.Clock).RecoverAsync(row);
        Assert.True(result.IsFailure);
        Assert.Equal(IntakeStatus.Failed, row.Status);
        Assert.Equal(0, f.Dispatcher.Calls);
    }

    [Fact]
    public async Task UnexpiredLease_DoesNotAcquireOrProbe()
    {
        var f = new IntakeTestFixture();
        var row = f.Row(f.Envelope());
        var probe = new CountingProbe();
        var result = await new IntakeRecoveryService(f.Store, [probe], f.Clock).RecoverAsync(row);
        Assert.True(result.IsFailure);
        Assert.Null(f.Store.AcquiredAttempt);
        Assert.Equal(0, probe.Calls);
    }

    private sealed class CountingProbe : IEffectCommittedProbe
    {
        public int Calls { get; private set; }
        public bool Committed { get; init; }
        public bool Handles(string commandType) => commandType == "test.create";
        public Task<Result<bool>> IsCommittedAsync(CommandIntakeRecord row, CancellationToken ct = default)
        { Calls++; return Task.FromResult(Result<bool>.Ok(Committed)); }
    }
}
