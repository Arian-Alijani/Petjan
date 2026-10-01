using System;
using System.Threading;
using System.Threading.Tasks;

namespace Animora.Desktop.Shell;

/// <summary>
/// Avalonia-free, latest-wins refresh lifecycle shared by pages. Every load belongs to a generation:
/// activating, deactivating or resetting bumps the generation and cancels the in-flight token, so a late result
/// of an older activation is never published and its completion can never clear the busy flag of a newer one.
/// Concurrent triggers coalesce into the in-flight load instead of issuing parallel queries, and a refresh
/// requested during a load is retained (re-run once) rather than dropped.
/// </summary>
/// <typeparam name="TSnapshot">The raw data returned by the service; same instance returned ⇒ nothing is republished.</typeparam>
/// <typeparam name="TView">The fully prepared, immutable presentation state published to the UI.</typeparam>
public sealed class PageRefreshCoordinator<TSnapshot, TView> : IDisposable
    where TSnapshot : class
    where TView : class
{
    /// <summary>Loads a snapshot; <c>force</c> asks for data that bypasses any cached copy.</summary>
    public delegate Task<TSnapshot> Fetch(CancellationToken ct, bool force);

    private readonly Fetch _fetch;
    private readonly Func<TSnapshot, TView> _prepare;
    private readonly Action<TView> _publish;
    private readonly Action<Exception>? _onFailed;
    private readonly Action? _onLoaded;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _loadCts;
    private Task? _inflight;
    private int _generation;
    private bool _inflightForced;
    private bool _rerunForced;
    private bool _disposed;
    private bool _active;
    private DateTimeOffset? _lastTick;
    private readonly TimeSpan _pollInterval;

    public PageRefreshCoordinator(Fetch fetch, Func<TSnapshot, TView> prepare, Action<TView> publish,
        Action<Exception>? onFailed = null, Action? onLoaded = null, TimeSpan? pollInterval = null)
    {
        _pollInterval = pollInterval ?? TimeSpan.FromMinutes(1);
        _onLoaded = onLoaded;
        _fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
        _prepare = prepare ?? throw new ArgumentNullException(nameof(prepare));
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _onFailed = onFailed;
    }

    /// <summary>Raised whenever <see cref="IsBusy"/> flips for the current generation.</summary>
    public event Action? StateChanged;

    /// <summary>True only while a load of the <em>current</em> generation is running.</summary>
    public bool IsBusy { get { lock (_gate) return _inflight is not null; } }

    /// <summary>Activation/deactivation/reset epochs so far (diagnostics and tests).</summary>
    public int Generation { get { lock (_gate) return _generation; } }

    /// <summary>The snapshot most recently published.</summary>
    public TSnapshot? Current { get; private set; }

    public DateTimeOffset? LastUpdated { get; private set; }

    /// <summary>A visit began: supersede anything in flight and guarantee a refresh for this visit.</summary>
    public Task ActivateAsync()
    {
        lock (_gate) { _active = true; _lastTick = null; }
        return Start(force: false, supersede: true);
    }

    /// <summary>One bounded active-page poll per interval (default one minute); timestamps are supplied by the scheduler.</summary>
    public Task TickAsync(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_active || _disposed) return Task.CompletedTask;
            if (_lastTick is { } last && now - last < _pollInterval) return Task.CompletedTask;
            _lastTick = now;
        }
        return RefreshAsync(force: true);
    }

    /// <summary>The page was left: cancel in-flight work; its result will never be published.</summary>
    public void Deactivate()
    {
        bool changed;
        lock (_gate) { _active = false; changed = CancelLocked(); }
        if (changed) StateChanged?.Invoke();
    }

    /// <summary>
    /// The signed-in identity/role changed: cancel anything in flight (a late response of the previous identity is
    /// never published) and forget the published snapshot so the next load always publishes, even if it were the
    /// same instance. The caller clears its own presentation state.
    /// </summary>
    public void Reset()
    {
        bool changed;
        lock (_gate)
        {
            changed = CancelLocked();
            Current = null;
            LastUpdated = null;
        }
        if (changed) StateChanged?.Invoke();
    }

    /// <summary>
    /// Requests a load. Concurrent callers share one query; a forced request that arrives during a non-forced
    /// load schedules exactly one forced re-run.
    /// </summary>
    public Task RefreshAsync(bool force = false) => Start(force, supersede: false);

    private Task Start(bool force, bool supersede)
    {
        TaskCompletionSource done;
        CancellationTokenSource cts;
        int generation;
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
            if (!supersede && _inflight is { } running)
            {
                if (force && !_inflightForced) _rerunForced = true;
                return running;
            }
            CancelLocked();
            done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            cts = _loadCts = new CancellationTokenSource();
            generation = ++_generation;
            _inflight = done.Task;
            _inflightForced = force;
            _rerunForced = false;
        }
        StateChanged?.Invoke();
        _ = RunAsync(done, cts, generation, force);
        return done.Task;
    }

    // Callers hold _gate. Returns whether a running load was cancelled.
    private bool CancelLocked()
    {
        var had = _inflight is not null;
        _generation++;
        _inflight = null;
        _rerunForced = false;
        var cts = _loadCts;
        _loadCts = null;
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        return had;
    }

    private async Task RunAsync(TaskCompletionSource done, CancellationTokenSource cts, int generation, bool force)
    {
        try
        {
            while (true)
            {
                await LoadOnceAsync(cts.Token, generation, force).ConfigureAwait(true);
                lock (_gate)
                {
                    if (generation != _generation || !_rerunForced) break;
                    _rerunForced = false;
                    _inflightForced = true;
                }
                force = true;
            }
        }
        finally
        {
            bool current;
            lock (_gate)
            {
                current = generation == _generation;
                if (current) { _inflight = null; _loadCts = null; }
            }
            if (current) StateChanged?.Invoke();
            done.TrySetResult();
            cts.Dispose();
        }
    }

    private async Task LoadOnceAsync(CancellationToken ct, int generation, bool force)
    {
        try
        {
            var snapshot = await _fetch(ct, force).ConfigureAwait(true);
            lock (_gate)
            {
                if (generation != _generation || ct.IsCancellationRequested) return;
            }
            // A cache hit returns the same immutable snapshot: nothing to rebuild or republish.
            if (ReferenceEquals(Current, snapshot)) { LastUpdated = DateTimeOffset.UtcNow; _onLoaded?.Invoke(); return; }
            var view = _prepare(snapshot);
            lock (_gate)
            {
                if (generation != _generation) return;
            }
            _publish(view);
            Current = snapshot;
            LastUpdated = DateTimeOffset.UtcNow;
            _onLoaded?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // Superseded or left: silent by contract, never a failure message.
        }
        catch (Exception ex)
        {
            bool current;
            lock (_gate) current = generation == _generation;
            if (current) _onFailed?.Invoke(ex);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            CancelLocked();
        }
    }
}
