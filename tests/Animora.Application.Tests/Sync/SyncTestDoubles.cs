using Animora.Domain.Results;
using Animora.Sync.Abstractions;
using Animora.Client.Services.Sync;

namespace Animora.Application.Tests.Sync;

/// <summary>Scripted state-sync engine: returns the queued results in order, then repeats the last one.</summary>
internal sealed class FakeStateSyncEngine : IStateSyncEngine
{
    private readonly Queue<Result<SyncResult>> _script = new();
    private Result<SyncResult>? _last;

    public bool IsConfigured { get; set; } = true;
    public int Pulls { get; private set; }

    public FakeStateSyncEngine Then(Result<SyncResult> next) { _script.Enqueue(next); return this; }

    public Task<Result<SyncResult>> PullAsync(SyncScope scope, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Pulls++;
        if (_script.Count > 0) _last = _script.Dequeue();
        return Task.FromResult(_last ?? Result<SyncResult>.Ok(SyncResult.Deferred(DateTimeOffset.UnixEpoch)));
    }

    public static Result<SyncResult> Ok(SyncOutcome outcome, DateTimeOffset now, string? detail = null) => outcome switch
    {
        SyncOutcome.Applied => Result<SyncResult>.Ok(SyncResult.Applied(5, 1, 3, now)),
        SyncOutcome.UpToDate => Result<SyncResult>.Ok(SyncResult.UpToDate(5, now)),
        SyncOutcome.ResyncRequired => Result<SyncResult>.Ok(SyncResult.ResyncRequired(detail ?? "epoch-changed", now)),
        _ => Result<SyncResult>.Ok(SyncResult.Deferred(now, detail)),
    };
}

/// <summary>Records requested delays and completes instantly; cancels the supplied source after <c>stopAfter</c> delays.</summary>
internal sealed class FakeDelayProvider(CancellationTokenSource? stop = null, int stopAfter = int.MaxValue) : IDelayProvider
{
    public List<TimeSpan> Delays { get; } = [];

    public Task Delay(TimeSpan delay, CancellationToken ct = default)
    {
        Delays.Add(delay);
        if (Delays.Count >= stopAfter) stop?.Cancel();
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

internal sealed class RecordingResyncHandler : ISyncResyncHandler
{
    public List<(SyncScope Scope, string Reason)> Calls { get; } = [];

    public Task<Result> HandleAsync(SyncScope scope, string reason, CancellationToken ct = default)
    {
        Calls.Add((scope, reason));
        return Task.FromResult(Result.Ok());
    }
}
