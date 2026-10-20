using Animora.Application.Commands;
using Animora.Contracts.Wire;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Sync.Custom;

/// <summary>Counts of one <see cref="SettlementReconciler"/> pass.</summary>
/// <param name="Examined">Stuck pending commands re-queried.</param>
/// <param name="Resettled">Commands that received an authoritative settlement and were applied.</param>
/// <param name="StillPending">Commands the server still defers (or that could not be reached) — left untouched for the next pass.</param>
/// <param name="Failed">Commands whose answer was refused (illegal transition or a transport/journal failure).</param>
public sealed record SettlementReconciliation(int Examined, int Resettled, int StillPending, int Failed);

/// <summary>
/// G05 settlement reconciliation. A command can sit in <c>PendingSettlement</c> after the server already settled it (response lost, client
/// crashed between send and apply). This service finds such commands (pending, not under a live claim, last attempt older than a grace window)
/// and idempotently re-submits the <b>same</b> envelope — same <c>(TenantId, OperationId)</c> and same fingerprint, so the server returns its
/// stored result and never repeats the effect — then applies the authoritative settlement. It is invoked by the existing processor/scheduler
/// (G03); it owns no worker of its own. Every local state change is validated through <see cref="SettlementTransitions.EnsureAllowed"/>.
/// </summary>
public sealed class SettlementReconciler(
    ICommandJournal journal,
    ICommandTransport transport,
    IClock clock,
    CommandIdRemapService? idRemap = null)
{
    /// <summary>Default time a command may stay pending since its last attempt before it is re-queried.</summary>
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromMinutes(2);

    private const int PageSize = 50;

    /// <summary>Re-queries up to <paramref name="max"/> stuck commands older than <paramref name="grace"/> (default <see cref="DefaultGrace"/>).</summary>
    public async Task<Result<SettlementReconciliation>> ReconcileAsync(int max = 50, TimeSpan? grace = null, CancellationToken ct = default)
    {
        if (max < 1) return Result<SettlementReconciliation>.Fail(ErrorCodes.ValidationFailed, "max < 1");

        var now = clock.UtcNow;
        var cutoff = now - (grace ?? DefaultGrace);
        int examined = 0, resettled = 0, stillPending = 0, failed = 0;

        for (var page = 1; examined < max; page++)
        {
            var batch = await journal.GetOldestUnsettledPageAsync(page, PageSize, ct).ConfigureAwait(false);
            if (batch.IsFailure) return Result<SettlementReconciliation>.Fail(batch.Error, batch.Detail);
            var paged = batch.Value!;

            // The journal page is already in strictly ascending global LocalSequence (the earliest stuck command is the most
            // likely to unblock dependents), so pages continue the same order and no in-memory re-sort is needed.
            for (var i = 0; i < paged.Items.Count; i++)
            {
                var record = paged.Items[i];
                if (examined >= max) break;
                if (!IsStuck(record, now, cutoff)) continue;

                examined++;
                ct.ThrowIfCancellationRequested();
                switch (await ReconcileOneAsync(record, ct).ConfigureAwait(false))
                {
                    case Step.Resettled: resettled++; break;
                    case Step.StillPending: stillPending++; break;
                    default: failed++; break;
                }
            }

            if (page * PageSize >= paged.TotalCount || paged.Items.Count == 0) break;
        }

        return Result<SettlementReconciliation>.Ok(new SettlementReconciliation(examined, resettled, stillPending, failed));
    }

    private enum Step { Resettled, StillPending, Failed }

    private static bool IsStuck(PendingCommandRecord r, DateTimeOffset now, DateTimeOffset cutoff)
    {
        if (r.Status != SettlementState.PendingSettlement) return false;
        if (r.ClaimedUntilUtc is { } lease && lease > now) return false; // a live processor pass owns it
        var reference = r.LastAttemptUtc ?? r.CreatedAtUtc;
        return reference <= cutoff;
    }

    private async Task<Step> ReconcileOneAsync(PendingCommandRecord record, CancellationToken ct)
    {
        // Same envelope, byte for byte: the idempotent retry contract (ARCHITECTURE.md §12.3). Never re-mint or rewrite here.
        var submitted = await transport.SubmitAsync(ToEnvelope(record), ct).ConfigureAwait(false);
        if (submitted.IsFailure) return Step.Failed;

        var settlement = submitted.Value!;
        if (settlement.Outcome == SettlementOutcome.Deferred) return Step.StillPending;

        var allowed = SettlementTransitions.EnsureAllowed(record.Status, TargetState(settlement.Outcome));
        if (allowed.IsFailure) return Step.Failed;

        var applied = await journal.ApplySettlementAsync(settlement, ct).ConfigureAwait(false);
        if (applied.IsFailure) return Step.Failed;

        idRemap?.Learn(record, settlement);
        return Step.Resettled;
    }

    private static SettlementState TargetState(SettlementOutcome outcome) => outcome switch
    {
        SettlementOutcome.Accepted => SettlementState.Settled,
        SettlementOutcome.Rejected => SettlementState.Rejected,
        SettlementOutcome.Conflict => SettlementState.Conflict,
        _ => SettlementState.PendingSettlement,
    };

    private static CommandEnvelope ToEnvelope(PendingCommandRecord c) => CommandEnvelope.Create(
        c.OperationId, c.TenantId, c.UserId, c.DeviceId, c.CommandType, c.AggregateType, c.AggregateId,
        c.PayloadJson, c.CommandFingerprint, c.CreatedAtUtc, c.LocalSequence, c.CausationId,
        c.DependsOnOperationId, c.ExpectedAggregateVersion,
        // CW-01: thread the locally-stored canonical server version as the precondition; CW-02/CW-05: rule-set version.
        expectedServerVersion: c.ServerVersion,
        ruleSetVersion: WireContract.RuleSetVersionString);
}
