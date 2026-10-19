using Animora.Contracts.Wire.Commands;

namespace Animora.LiveTest.Client;

/// <summary>
/// Result of pushing one command through the real production transport. Exactly one of
/// <see cref="Settlement"/> / <see cref="TransportError"/> is set.
/// </summary>
public sealed record SubmitOutcome(
    CommandEnvelope Envelope,
    SettlementResult? Settlement,
    string? TransportError,
    TimeSpan Elapsed)
{
    /// <summary>The server accepted and applied the command.</summary>
    public bool IsAccepted => Settlement?.Outcome == SettlementOutcome.Accepted;

    /// <summary>A stale-version conflict (an expected, assertable outcome — not a defect).</summary>
    public bool IsConflict => Settlement?.Outcome == SettlementOutcome.Conflict;

    /// <summary>Back-pressure / transient transport answer: retry the SAME operation id later.</summary>
    public bool IsDeferred => Settlement?.Outcome == SettlementOutcome.Deferred;

    /// <summary>The server refused the command with a business or validation error.</summary>
    public bool IsRejected => Settlement?.Outcome == SettlementOutcome.Rejected;

    /// <summary>Server-assigned id of the affected aggregate (when settled).</summary>
    public string? ServerEntityId => Settlement?.ServerEntityId;

    /// <summary>Server version token of the aggregate after the command (when settled).</summary>
    public string? ServerVersion => Settlement?.ServerVersion;

    /// <summary>One-line English description for reports.</summary>
    public string Describe() => Settlement is { } s
        ? $"op={Envelope.OperationId} type={Envelope.CommandType} outcome={s.Outcome} error={s.ErrorCode ?? "-"} entity={s.ServerEntityId ?? "-"} version={s.ServerVersion ?? "-"} detail={s.Detail ?? "-"}"
        : $"op={Envelope.OperationId} type={Envelope.CommandType} transport-error={TransportError}";
}

/// <summary>Result of one incremental/snapshot pull of the state feed.</summary>
public sealed record PullResult(PullKind Kind, int RecordsApplied, long Watermark, bool WasSnapshot, string? Error, TimeSpan Elapsed)
{
    /// <summary>The pull completed (applied or already up to date).</summary>
    public bool IsOk => Kind is PullKind.Applied or PullKind.UpToDate;
}

/// <summary>Disposition of a pull.</summary>
public enum PullKind
{
    /// <summary>New records were downloaded and applied.</summary>
    Applied,

    /// <summary>The replica already matched the server watermark.</summary>
    UpToDate,

    /// <summary>The server stayed saturated (503 + Retry-After) through every retry: expected back-pressure.</summary>
    Busy,

    /// <summary>Protocol, auth or network failure — a defect to investigate.</summary>
    Failed,
}
