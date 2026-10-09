using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Commands;

namespace Animora.Server.Commands;

/// <summary>Recovery seam only; S13 supplies tenant-scoped scheduling. Every takeover is fenced.</summary>
internal sealed class IntakeRecoveryService(ICommandIntakeStore store, IEnumerable<IEffectCommittedProbe> probes, IClock clock)
{
    public const string LeaseOwner = "command-intake";
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);

    public async Task<Result<string>> RecoverAsync(CommandIntakeRecord row, CancellationToken ct = default)
    {
        DateTimeOffset now = clock.UtcNow;
        if (row.Status != IntakeStatus.Failed && (row.Status != IntakeStatus.InProgress || row.LeaseUntilUtc >= now))
            return Result<string>.Fail(ErrorCodes.CommandInProgress);
        string attempt = IdGenerator.NewId(now);
        if (!await store.TryAcquireAttemptAsync(row.OperationId, row.AttemptId, attempt, LeaseOwner,
                now, now + LeaseDuration, ct).ConfigureAwait(false))
            return Result<string>.Fail(ErrorCodes.CommandInProgress, "intake_fenced");
        IEffectCommittedProbe probe = probes.LastOrDefault(p => p is not NoProbe && p.Handles(row.CommandType)) ?? new NoProbe();
        Result<bool> evidence = await probe.IsCommittedAsync(row, ct).ConfigureAwait(false);
        if (evidence.IsFailure || evidence.Value || (row.Status == IntakeStatus.InProgress && row.AttemptCount >= 3))
        {
            bool failed = await store.MarkFailedAsync(row.OperationId, attempt,
                evidence.Value ? "effect evidence requires reconciliation" : "expired lease limit or probe failure", ct).ConfigureAwait(false);
            return Result<string>.Fail(ErrorCodes.CommandInProgress, failed ? "recovery_requires_attention" : "intake_fenced");
        }
        return Result<string>.Ok(attempt);
    }
}
