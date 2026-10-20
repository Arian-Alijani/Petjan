using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>
/// B02 acceptance: commands are submitted in <c>LocalSequence</c> order; a command whose
/// <c>DependsOnOperationId</c> is unsettled is skipped with <c>DEPENDENCY_UNSETTLED</c>; backoff grows
/// exponentially with jitter within ±20 %; attempt 11 moves the row to <c>NeedsAttention</c>; with
/// <see cref="NullCommandTransport"/> every command stays <c>PendingSettlement</c> (offline stays functional).
/// </summary>
public sealed class CommandProcessorTests
{
    private static CommandEnvelope Envelope(long sequence, string? dependsOn = null) => CommandEnvelope.Create(
        IdGenerator.NewId(), "T1", "U1", "D1", "sales.invoice.post", "Invoice", "INV-1", "{}",
        "fp", DateTimeOffset.UnixEpoch, sequence, dependsOnOperationId: dependsOn);

    private static (LocalCommandProcessor Processor, FakeCommandJournal Journal, FakeCommandTransport Transport) Build(
        ICommandTransport? transport = null)
    {
        var sink = new RecordingIntegritySink();
        var journal = new FakeCommandJournal(sink);
        var fake = transport as FakeCommandTransport ?? new FakeCommandTransport();
        return (new LocalCommandProcessor(journal, transport ?? fake, new FixedClock()), journal, fake);
    }

    [Fact]
    public async Task Pass_SubmitsInLocalSequenceOrder()
    {
        var (processor, journal, transport) = Build();
        journal.Seed(Envelope(1));
        journal.Seed(Envelope(2));
        journal.Seed(Envelope(3));
        journal.NextClaim.AddRange(journal.Rows.OrderBy(r => r.LocalSequence));

        var pass = await processor.RunPassAsync();

        Assert.True(pass.IsSuccess);
        Assert.Equal(3, pass.Value!.Settled);
        Assert.Equal([1L, 2L, 3L], transport.Submitted.Select(e => e.LocalSequence).ToArray());
        Assert.All(journal.Rows, r => Assert.Equal(SettlementState.Settled, r.Status));
    }

    [Fact]
    public async Task UnsettledDependency_IsSkippedWithDependencyUnsettled()
    {
        var (processor, journal, transport) = Build();
        var prerequisite = Envelope(1);
        journal.Seed(prerequisite, SettlementState.PendingSettlement); // never settled
        var dependent = Envelope(2, dependsOn: prerequisite.OperationId);
        journal.Seed(dependent);
        journal.NextClaim.Add(journal.Rows.Single(r => r.OperationId == dependent.OperationId));

        var pass = await processor.RunPassAsync();

        Assert.Equal(1, pass.Value!.SkippedUnsettledDependency);
        Assert.Empty(transport.Submitted);
        Assert.Contains(journal.Releases, r => r.Error == ErrorCodes.DependencyUnsettled);
        // A deferral never consumes the retry budget.
        Assert.Equal(0, journal.Rows.Single(r => r.OperationId == dependent.OperationId).Attempts);
    }

    [Theory]
    [InlineData(SettlementState.Rejected)]
    [InlineData(SettlementState.Conflict)]
    public async Task RefusedDependency_RejectsDependentWithoutUploading(SettlementState prerequisiteState)
    {
        var (processor, journal, transport) = Build();
        var prerequisite = Envelope(1);
        journal.Seed(prerequisite, prerequisiteState);
        var dependent = Envelope(2, dependsOn: prerequisite.OperationId);
        journal.Seed(dependent);
        journal.NextClaim.Add(journal.Rows.Single(r => r.OperationId == dependent.OperationId));

        var pass = await processor.RunPassAsync();

        Assert.True(pass.IsSuccess);
        Assert.Empty(transport.Submitted);
        var settlement = Assert.Single(journal.AppliedSettlements);
        Assert.Equal(ErrorCodes.DependencyRejected.Code, settlement.ErrorCode);
        Assert.Equal(SettlementState.Rejected, journal.Rows.Single(r => r.OperationId == dependent.OperationId).Status);
    }

    [Fact]
    public async Task NullTransport_LeavesEveryCommandPendingSettlement()
    {
        var (processor, journal, _) = Build(new NullCommandTransport());
        journal.Seed(Envelope(1));
        journal.Seed(Envelope(2));
        journal.NextClaim.AddRange(journal.Rows);

        var pass = await processor.RunPassAsync();

        Assert.Equal(2, pass.Value!.Deferred);
        Assert.All(journal.Rows, r => Assert.Equal(SettlementState.PendingSettlement, r.Status));
    }

    [Fact]
    public async Task ExhaustedRetries_MoveRowToNeedsAttention()
    {
        var (processor, journal, transport) = Build();
        transport.OnSubmit = e => Result<SettlementResult>.Fail(ErrorCodes.StoreUnavailable);
        journal.Seed(Envelope(1), attempts: RetryPolicy.MaxAttempts); // 10 attempts already spent
        journal.NextClaim.Add(journal.Rows[0]);

        var pass = await processor.RunPassAsync();

        Assert.Equal(1, pass.Value!.Failed);
        Assert.Equal(SettlementState.NeedsAttention, journal.Rows[0].Status); // the 11th attempt
        Assert.Equal(RetryPolicy.MaxAttempts + 1, journal.Rows[0].Attempts);
    }

    [Fact]
    public void Backoff_GrowsExponentially_CappedAt15Minutes()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), RetryPolicy.NextDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(4), RetryPolicy.NextDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(8), RetryPolicy.NextDelay(3));
        Assert.Equal(TimeSpan.FromMinutes(15), RetryPolicy.NextDelay(10));
        Assert.Equal(TimeSpan.FromMinutes(15), RetryPolicy.NextDelay(50));
    }

    [Fact]
    public void Backoff_JitterStaysWithinTwentyPercent()
    {
        for (var attempts = 1; attempts <= 10; attempts++)
        {
            var baseDelay = RetryPolicy.NextDelay(attempts).TotalSeconds;
            Assert.Equal(baseDelay * 0.8, RetryPolicy.NextDelay(attempts, 0.0).TotalSeconds, 3);
            Assert.Equal(baseDelay * 1.2, RetryPolicy.NextDelay(attempts, 0.999999).TotalSeconds, 3);
            var mid = RetryPolicy.NextDelay(attempts, 0.5).TotalSeconds;
            Assert.InRange(mid, baseDelay * 0.8, baseDelay * 1.2);
        }
    }

    [Fact]
    public void RetryBudget_ExhaustsAtMaxAttempts()
    {
        // Authoritative policy (mirrored by the persisted PendingCommand/OutboxMessage paths): the budget is
        // spent once Attempts reaches MaxAttempts, so a command seeded with MaxAttempts already spent is moved
        // to NeedsAttention by its very next failure.
        Assert.False(RetryPolicy.IsExhausted(RetryPolicy.MaxAttempts - 1));
        Assert.True(RetryPolicy.IsExhausted(RetryPolicy.MaxAttempts));
    }
}
