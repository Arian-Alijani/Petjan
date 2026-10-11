using Animora.Contracts.Wire.Commands;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Sync.Custom;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>G05: stuck-pending re-query and authoritative settlement.</summary>
public sealed class SettlementReconcilerTests
{
    private static readonly FixedClock Clock = new(FeedFixtures.Now);

    [Fact]
    public async Task StuckDeferred_ReQuery_PullsAuthoritativeSettlement()
    {
        var journal = new FakeJournal([CommandFixtures.Record("OP1", aggregateId: "LOCAL-PET")]);
        var transport = new ScriptedTransport(e => CommandFixtures.Accepted(e.OperationId, "SRV-PET"));
        var remap = new CommandIdRemapService(new SingleClientFactory(new HttpClient()), FeedFixtures.Options());
        var reconciler = new SettlementReconciler(journal, transport, Clock, remap);

        var result = await reconciler.ReconcileAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(new SettlementReconciliation(1, 1, 0, 0), result.Value);
        Assert.Equal("OP1", Assert.Single(journal.Applied).OperationId);
        Assert.Equal("SRV-PET", remap.Resolve(CommandFixtures.Tenant, "LOCAL-PET"));
        // Idempotent resubmission: the very same envelope (same operation id + fingerprint) — never re-minted.
        var sent = Assert.Single(transport.Sent);
        Assert.Equal(journal.Rows[0].CommandFingerprint, sent.CommandFingerprint);
    }

    [Fact]
    public async Task ServerStillDeferring_LeavesCommandPending()
    {
        var journal = new FakeJournal([CommandFixtures.Record("OP1")]);
        var transport = new ScriptedTransport(e => new SettlementResult(e.OperationId, SettlementOutcome.Deferred, null, null, null, null,
            FeedFixtures.Now, FinancialEffect.Unknown, null));
        var result = await new SettlementReconciler(journal, transport, Clock).ReconcileAsync();
        Assert.Equal(new SettlementReconciliation(1, 0, 1, 0), result.Value);
        Assert.Empty(journal.Applied);
    }

    [Fact]
    public async Task OnlyStuckPendingCommands_AreRequeried()
    {
        var fresh = FeedFixtures.Now.AddSeconds(-5);
        var journal = new FakeJournal(
        [
            CommandFixtures.Record("STUCK", sequence: 1),
            CommandFixtures.Record("FRESH", lastAttempt: fresh, sequence: 2),                                   // inside grace
            CommandFixtures.Record("LEASED", claimedUntil: FeedFixtures.Now.AddMinutes(1), sequence: 3),         // live processor pass
            CommandFixtures.Record("CONFLICT", SettlementState.Conflict, sequence: 4),                           // not pending
            CommandFixtures.Record("ATTN", SettlementState.NeedsAttention, sequence: 5),
        ]);
        var transport = new ScriptedTransport(e => CommandFixtures.Accepted(e.OperationId));

        var result = await new SettlementReconciler(journal, transport, Clock).ReconcileAsync();

        Assert.Equal(1, result.Value!.Examined);
        Assert.Equal(["STUCK"], transport.Sent.Select(e => e.OperationId));
    }

    [Fact]
    public async Task RespectsMaxPerPass()
    {
        var journal = new FakeJournal(Enumerable.Range(1, 5).Select(i => CommandFixtures.Record($"OP{i}", sequence: i)));
        var transport = new ScriptedTransport(e => CommandFixtures.Accepted(e.OperationId));
        var result = await new SettlementReconciler(journal, transport, Clock).ReconcileAsync(max: 2);
        Assert.Equal(2, result.Value!.Examined);
        Assert.Equal(["OP1", "OP2"], transport.Sent.Select(e => e.OperationId)); // oldest sequence first
    }

    [Fact]
    public async Task IllegalTransition_Rejected_ByEnsureAllowed()
    {
        // A command already Settled can never be re-settled as Rejected: the table forbids it, so nothing is applied.
        Assert.True(SettlementTransitions.EnsureAllowed(SettlementState.Settled, SettlementState.Rejected).IsFailure);

        var journal = new FakeJournal([CommandFixtures.Record("OP1", SettlementState.PendingSettlement)]);
        var transport = new ScriptedTransport(e => CommandFixtures.Accepted(e.OperationId));
        var ok = await new SettlementReconciler(journal, transport, Clock).ReconcileAsync();
        Assert.Equal(1, ok.Value!.Resettled); // the legal move Pending → Settled is applied
    }

    [Fact]
    public async Task TransportFailure_CountsAsFailed_NothingApplied()
    {
        var journal = new FakeJournal([CommandFixtures.Record("OP1")]);
        var transport = new ScriptedTransport(_ => Result<SettlementResult>.Fail(ErrorCodes.Unauthorized));
        var result = await new SettlementReconciler(journal, transport, Clock).ReconcileAsync();
        Assert.Equal(new SettlementReconciliation(1, 0, 0, 1), result.Value);
        Assert.Empty(journal.Applied);
    }

    [Fact]
    public async Task InvalidMax_FailsValidation()
    {
        var result = await new SettlementReconciler(new FakeJournal([]), new ScriptedTransport(_ => default), Clock).ReconcileAsync(0);
        Assert.Equal(ErrorCodes.ValidationFailed, result.Error);
    }
}
