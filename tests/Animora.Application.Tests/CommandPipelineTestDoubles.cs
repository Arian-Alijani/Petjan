using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Application.UseCases.Sync;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Primitives;
using Animora.Domain.Results;

namespace Animora.Application.Tests;

/// <summary>Counts integrity alerts so a fingerprint mismatch can be proven to raise exactly one (A-RULE 019).</summary>
internal sealed class RecordingIntegritySink : IIntegrityAlertSink
{
    public int Count { get; private set; }
    public string? LastCode { get; private set; }

    public void Raise(string code, string detail)
    {
        Count++;
        LastCode = code;
    }
}

/// <summary>
/// An in-memory <see cref="ICommandJournal"/> for the Application-layer tests. It reuses the real
/// <see cref="CommandFingerprint"/> so the replay-vs-mismatch decision under test is the production one.
/// </summary>
internal sealed class FakeCommandJournal(IIntegrityAlertSink sink) : ICommandJournal
{
    private readonly List<PendingCommandRecord> _rows = [];

    public IReadOnlyList<PendingCommandRecord> Rows => _rows;

    /// <summary>Rows returned by the next <see cref="ClaimBatchAsync"/> call, in the order supplied.</summary>
    public List<PendingCommandRecord> NextClaim { get; } = [];

    public List<(string OperationId, ErrorCode Error, TimeSpan Backoff)> Releases { get; } = [];

    public List<SettlementResult> AppliedSettlements { get; } = [];

    public Task<Result<PendingCommandRecord>> EnqueueAsync(CommandEnvelope envelope, CancellationToken ct = default)
    {
        var existing = _rows.FirstOrDefault(r => r.OperationId == envelope.OperationId);
        if (existing is not null)
        {
            if (string.Equals(existing.CommandFingerprint, envelope.CommandFingerprint, StringComparison.Ordinal))
                return Task.FromResult(Result<PendingCommandRecord>.Ok(existing));
            sink.Raise(ErrorCodes.IdempotencyFingerprintMismatch.Code, envelope.OperationId);
            return Task.FromResult(Result<PendingCommandRecord>.Fail(ErrorCodes.IdempotencyFingerprintMismatch));
        }

        var row = Record(envelope, SettlementState.LocallyPosted);
        _rows.Add(row);
        return Task.FromResult(Result<PendingCommandRecord>.Ok(row));
    }

    public Task<Result<PendingCommandRecord?>> FindByOperationIdAsync(string operationId, CancellationToken ct = default) =>
        Task.FromResult(Result<PendingCommandRecord?>.Ok(_rows.FirstOrDefault(r => r.OperationId == operationId)));

    public Task<Result> ApplySettlementAsync(SettlementResult result, CancellationToken ct = default)
    {
        AppliedSettlements.Add(result);
        var index = _rows.FindIndex(r => r.OperationId == result.OperationId);
        if (index < 0) return Task.FromResult(Result.Fail(ErrorCodes.NotFound));
        var status = result.Outcome switch
        {
            SettlementOutcome.Accepted => SettlementState.Settled,
            SettlementOutcome.Rejected => SettlementState.Rejected,
            SettlementOutcome.Conflict => SettlementState.Conflict,
            _ => SettlementState.PendingSettlement,
        };
        _rows[index] = _rows[index] with { Status = status };
        return Task.FromResult(Result.Ok());
    }

    public Task<Result<IReadOnlyList<PendingCommandRecord>>> ClaimBatchAsync(int max, TimeSpan lease,
        CancellationToken ct = default)
    {
        var claimed = NextClaim.Take(max).ToArray();
        NextClaim.RemoveRange(0, claimed.Length);
        foreach (var row in claimed)
        {
            var index = _rows.FindIndex(r => r.OperationId == row.OperationId);
            if (index >= 0) _rows[index] = _rows[index] with { Status = SettlementState.PendingSettlement };
        }
        IReadOnlyList<PendingCommandRecord> result = claimed;
        return Task.FromResult(Result<IReadOnlyList<PendingCommandRecord>>.Ok(result));
    }

    public Task<Result> ReleaseAsync(string operationId, ErrorCode error, TimeSpan backoff, CancellationToken ct = default)
    {
        Releases.Add((operationId, error, backoff));
        var index = _rows.FindIndex(r => r.OperationId == operationId);
        if (index >= 0)
        {
            var isFailure = !error.IsNone && error != ErrorCodes.DependencyUnsettled;
            var attempts = isFailure ? _rows[index].Attempts + 1 : _rows[index].Attempts;
            var status = isFailure && RetryPolicy.IsExhausted(attempts)
                ? SettlementState.NeedsAttention : SettlementState.PendingSettlement;
            _rows[index] = _rows[index] with { Attempts = attempts, Status = status };
        }
        return Task.FromResult(Result.Ok());
    }

    public Task<Result<IReadOnlyList<PendingCommandRecord>>> GetUnsettledForAggregateAsync(string aggregateType,
        string aggregateId, CancellationToken ct = default)
    {
        IReadOnlyList<PendingCommandRecord> rows = _rows
            .Where(r => r.AggregateType == aggregateType && r.AggregateId == aggregateId).ToArray();
        return Task.FromResult(Result<IReadOnlyList<PendingCommandRecord>>.Ok(rows));
    }

    public Task<Result<PendingCommandSummary>> GetSummaryAsync(CancellationToken ct = default) =>
        Task.FromResult(Result<PendingCommandSummary>.Ok(
            new PendingCommandSummary(_rows.Count, 0, 0, 0, 0, _rows.Count == 0 ? null : _rows.Min(r => r.CreatedAtUtc))));

    public Task<Result<PagedResult<PendingCommandRecord>>> GetUnsettledPageAsync(int page, int pageSize,
        CancellationToken ct = default)
    {
        IReadOnlyList<PendingCommandRecord> rows = _rows.ToArray();
        return Task.FromResult(Result<PagedResult<PendingCommandRecord>>.Ok(
            new PagedResult<PendingCommandRecord>(rows, rows.Count, page, pageSize)));
    }

    /// <summary>Seeds a row directly (test setup), bypassing the enqueue path.</summary>
    public PendingCommandRecord Seed(CommandEnvelope envelope, SettlementState status = SettlementState.LocallyPosted,
        int attempts = 0)
    {
        var row = Record(envelope, status) with { Attempts = attempts };
        _rows.Add(row);
        return row;
    }

    private static PendingCommandRecord Record(CommandEnvelope e, SettlementState status) => new(
        Id: IdGenerator.NewId(), OperationId: e.OperationId, TenantId: e.TenantId, UserId: e.UserId,
        DeviceId: e.DeviceId, CommandType: e.CommandType, AggregateType: e.AggregateType, AggregateId: e.AggregateId,
        PayloadJson: e.PayloadJson, CommandFingerprint: e.CommandFingerprint, LocalSequence: e.LocalSequence,
        Status: status, CausationId: e.CausationId, DependsOnOperationId: e.DependsOnOperationId,
        ExpectedAggregateVersion: e.ExpectedAggregateVersion, CreatedAtUtc: e.CreatedAtUtc, LastAttemptUtc: null,
        Attempts: 0, NextAttemptUtc: null, LastErrorCode: null, LastErrorDetail: null,
        FinancialEffect: FinancialEffect.Unknown, ResponsibleRole: null, SettledAtUtc: null, ServerEntityId: null,
        ServerVersion: null, ClaimToken: null, ClaimedUntilUtc: null);
}

/// <summary>A monotonic in-memory sequence allocator for the Application-layer tests.</summary>
internal sealed class CountingSequenceAllocator : ILocalSequenceAllocator
{
    private long _value;
    public Task<Result<long>> NextAsync(CancellationToken ct = default) => Task.FromResult(Result<long>.Ok(++_value));
}

/// <summary>A transport whose per-call outcome the test controls; records the envelopes it saw, in order.</summary>
internal sealed class FakeCommandTransport : ICommandTransport
{
    public Func<CommandEnvelope, Result<SettlementResult>> OnSubmit { get; set; } =
        e => Result<SettlementResult>.Ok(Accepted(e.OperationId));

    public List<CommandEnvelope> Submitted { get; } = [];

    public Task<Result<SettlementResult>> SubmitAsync(CommandEnvelope envelope, CancellationToken ct = default)
    {
        Submitted.Add(envelope);
        return Task.FromResult(OnSubmit(envelope));
    }

    public static SettlementResult Accepted(string operationId) => new(operationId, SettlementOutcome.Accepted,
        null, null, "SRV-1", "v1", DateTimeOffset.UnixEpoch, FinancialEffect.Applied, null);
}
