using Animora.Application.Abstractions;
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
    ICommandTransport transport)
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

            if (await HasUnsettledDependencyAsync(command, ct).ConfigureAwait(false))
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
    /// A dependency is "unsettled" unless it is <c>Settled</c> — an unknown, pending, rejected, conflicted or
    /// reversed prerequisite all defer (A-RULE 021: dependency ordering is resolved server-side only).
    /// </summary>
    private async Task<bool> HasUnsettledDependencyAsync(PendingCommandRecord command, CancellationToken ct)
    {
        if (command.DependsOnOperationId is not { Length: > 0 } dependency) return false;
        var prerequisite = await journal.FindByOperationIdAsync(dependency, ct).ConfigureAwait(false);
        return prerequisite.IsFailure || prerequisite.Value is null
            || prerequisite.Value.Status != SettlementState.Settled;
    }

    private Task<Result> ReleaseAsync(PendingCommandRecord command, ErrorCode error, CancellationToken ct)
    {
        var backoff = RetryPolicy.NextDelay(command.Attempts + 1, Random.Shared.NextDouble());
        return journal.ReleaseAsync(command.OperationId, error, backoff, ct);
    }

    private static CommandEnvelope ToEnvelope(PendingCommandRecord c) => CommandEnvelope.Create(
        c.OperationId, c.TenantId, c.UserId, c.DeviceId, c.CommandType, c.AggregateType, c.AggregateId,
        c.PayloadJson, c.CommandFingerprint, c.CreatedAtUtc, c.LocalSequence, c.CausationId,
        c.DependsOnOperationId, c.ExpectedAggregateVersion);
}
