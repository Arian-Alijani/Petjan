using Animora.Application.Abstractions;
using Animora.Contracts.Wire;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Application.Commands;

/// <summary>
/// The outcome of one processor pass: how many commands were claimed and how each ended.
/// </summary>
public sealed record CommandProcessorPass(
    int Claimed,
    int Settled,
    int Deferred,
    int Failed,
    int SkippedUnsettledDependency);

/// <summary>
/// The resumable local command processor (ARCHITECTURE.md §8.3/§8.4, A-RULE 021/022/025/026): claim a batch,
/// process it in <c>LocalSequence</c> order, submit each through <see cref="ICommandTransport"/>, and settle
/// what comes back. It never reorders locally — an unsettled dependency is deferred with
/// <c>DEPENDENCY_UNSETTLED</c> and the server decides the final order. Background scheduling of this pass is
/// E04's; here it is a plain, testable method.
/// </summary>
public sealed class LocalCommandProcessor(
    ICommandJournal journal,
    ICommandTransport transport,
    IClock clock)
{
    private static int _activePasses;

    /// <summary>True while at least one bounded command pass is active.</summary>
    public static bool IsPassRunning => Volatile.Read(ref _activePasses) > 0;

    /// <summary>Rows claimed per pass; a bounded batch keeps the pass short and resumable.</summary>
    public const int DefaultBatchSize = 50;

    /// <summary>Claim lease; an expired lease is reclaimable, so a crash mid-pass strands nothing (A-RULE 026).</summary>
    public static readonly TimeSpan DefaultLease = TimeSpan.FromSeconds(60);

    public async Task<Result<CommandProcessorPass>> RunPassAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref _activePasses);
        try
        {
            return await RunPassCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _activePasses);
        }
    }

    private async Task<Result<CommandProcessorPass>> RunPassCoreAsync(CancellationToken ct)
    {
        var claimed = await journal.ClaimBatchAsync(DefaultBatchSize, DefaultLease, ct).ConfigureAwait(false);
        if (claimed.IsFailure) return Result<CommandProcessorPass>.Fail(claimed.Error, claimed.Detail);

        // ClaimBatchAsync returns rows already ordered by LocalSequence.
        var batch = claimed.Value!;
        int settled = 0, deferred = 0, failed = 0, skipped = 0;

        foreach (var command in batch)
        {
            ct.ThrowIfCancellationRequested();

            var dependency = await DependencyStateAsync(command, ct).ConfigureAwait(false);
            if (dependency == DependencyState.Refused)
            {
                // D06: a refused prerequisite can never be satisfied, so the dependent becomes terminal too —
                // the same DEPENDENCY_REJECTED answer the server gives — instead of being deferred forever.
                var refused = await journal.ApplySettlementAsync(DependencyRejected(command), ct).ConfigureAwait(false);
                if (refused.IsFailure) return Result<CommandProcessorPass>.Fail(refused.Error, refused.Detail);
                settled++;
                continue;
            }

            if (dependency == DependencyState.Unsettled)
            {
                var release = await ReleaseAsync(command, ErrorCodes.DependencyUnsettled, ct).ConfigureAwait(false);
                if (release.IsFailure) return Result<CommandProcessorPass>.Fail(release.Error, release.Detail);
                skipped++;
                continue;
            }

            var submitted = await transport.SubmitAsync(ToEnvelope(command), ct).ConfigureAwait(false);
            if (submitted.IsFailure)
            {
                var release = await ReleaseAsync(command, submitted.Error, ct).ConfigureAwait(false);
                if (release.IsFailure) return Result<CommandProcessorPass>.Fail(release.Error, release.Detail);
                failed++;
                continue;
            }

            var settlement = submitted.Value!;
            if (settlement.Outcome == SettlementOutcome.Deferred)
            {
                // Not a failure: the command stays pending and is retried unchanged, with no error recorded.
                var release = await ReleaseAsync(command, ErrorCode.None, ct).ConfigureAwait(false);
                if (release.IsFailure) return Result<CommandProcessorPass>.Fail(release.Error, release.Detail);
                deferred++;
                continue;
            }

            var applied = await journal.ApplySettlementAsync(settlement, ct).ConfigureAwait(false);
            if (applied.IsFailure) return Result<CommandProcessorPass>.Fail(applied.Error, applied.Detail);
            settled++;
        }

        return Result<CommandProcessorPass>.Ok(new CommandProcessorPass(batch.Count, settled, deferred, failed, skipped));
    }

    /// <summary>
    /// A dependency is satisfied only when it is <c>Settled</c>. A <c>Rejected</c>, <c>Conflict</c> or
    /// <c>Reversed</c> prerequisite is final (it mirrors the server's own DEPENDENCY_REJECTED rule), so the
    /// dependent is refused; an unknown, pending or needs-attention prerequisite defers (A-RULE 021).
    /// </summary>
    private async Task<DependencyState> DependencyStateAsync(PendingCommandRecord command, CancellationToken ct)
    {
        if (command.DependsOnOperationId is not { Length: > 0 } dependency) return DependencyState.None;
        var prerequisite = await journal.FindByOperationIdAsync(dependency, ct).ConfigureAwait(false);
        if (prerequisite.IsFailure || prerequisite.Value is null) return DependencyState.Unsettled;
        return prerequisite.Value.Status switch
        {
            SettlementState.Settled => DependencyState.None,
            SettlementState.Rejected or SettlementState.Conflict or SettlementState.Reversed => DependencyState.Refused,
            _ => DependencyState.Unsettled,
        };
    }

    private SettlementResult DependencyRejected(PendingCommandRecord command) => new(
        command.OperationId,
        SettlementOutcome.Rejected,
        ErrorCodes.DependencyRejected.Code,
        "DependsOn=" + command.DependsOnOperationId,
        ServerEntityId: null,
        ServerVersion: null,
        SettledAtUtc: clock.UtcNow,
        FinancialEffect.NotApplied,
        ResponsibleRole: null);

    private enum DependencyState { None, Unsettled, Refused }

    private Task<Result> ReleaseAsync(PendingCommandRecord command, ErrorCode error, CancellationToken ct)
    {
        var backoff = RetryPolicy.NextDelay(command.Attempts + 1, Random.Shared.NextDouble());
        return journal.ReleaseAsync(command.OperationId, error, backoff, ct);
    }

    private static CommandEnvelope ToEnvelope(PendingCommandRecord c) => CommandEnvelope.Create(
        c.OperationId, c.TenantId, c.UserId, c.DeviceId, c.CommandType, c.AggregateType, c.AggregateId,
        c.PayloadJson, c.CommandFingerprint, c.CreatedAtUtc, c.LocalSequence, c.CausationId,
        c.DependsOnOperationId, c.ExpectedAggregateVersion,
        // CW-01: carry the canonical server version the client last observed for this aggregate (null until the
        // first settlement stores one) so the server can precondition on it; CW-02/CW-05: stamp the rule-set version.
        expectedServerVersion: c.ServerVersion,
        ruleSetVersion: WireContract.RuleSetVersionString);
}
