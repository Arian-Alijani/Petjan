using System.Diagnostics;
using Animora.Server.Observability;
using Microsoft.Extensions.Options;

namespace Animora.Server.Hosting;

/// <summary>Outcome of one readiness evaluation (names only - never exception text).</summary>
internal sealed record ReadinessReport(IReadOnlyList<string> Failed, IReadOnlyList<string> Degraded)
{
    /// <summary>True when no REQUIRED probe failed.</summary>
    public bool IsReady => Failed.Count == 0;
}

/// <summary>
/// BUG-HEALTH-01 / IMP-10: evaluates the readiness probes at most once per
/// <c>Server:Health:ReadinessCacheSeconds</c> and serves the cached report to every poller in between.
/// <list type="bullet">
/// <item>Anonymous, un-rate-limited <c>/health/ready</c> polling therefore costs ONE probe round (DB, key ring,
/// schema, data root) per TTL regardless of poll frequency.</item>
/// <item>Concurrent callers on an expired entry share a single in-flight evaluation (single-flight), and the
/// probes inside a round run in parallel instead of serially.</item>
/// <item>The first healthy evaluation latches <see cref="Started"/>, which backs <c>/health/startup</c>.</item>
/// </list>
/// A TTL of <c>0</c> disables caching (every call evaluates).
/// </summary>
internal sealed class ReadinessEvaluator(IEnumerable<IReadinessProbe> probes, ReadinessSnapshot snapshot, IOptions<ServerOptions> options)
{
    private readonly IReadinessProbe[] _probes = probes.ToArray();
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(options.Value.Health.ReadinessCacheSeconds);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile CachedReport? _cached;
    private volatile bool _started;

    private sealed record CachedReport(ReadinessReport Report, long Timestamp);

    /// <summary>True once every required probe has been healthy at least once since process start.</summary>
    public bool Started => _started;

    /// <summary>Returns the cached report when fresh, otherwise runs (exactly one) new probe round.</summary>
    public async Task<ReadinessReport> EvaluateAsync(CancellationToken ct)
    {
        if (TryFresh(out var fresh))
        {
            return fresh;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Another caller may have refreshed while this one waited for the gate.
            if (TryFresh(out fresh))
            {
                return fresh;
            }

            var report = await RunProbesAsync(ct).ConfigureAwait(false);
            _cached = new CachedReport(report, Stopwatch.GetTimestamp());
            if (report.IsReady)
            {
                _started = true;
            }

            return report;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryFresh(out ReadinessReport report)
    {
        var cached = _cached;
        if (cached is not null && _ttl > TimeSpan.Zero && Stopwatch.GetElapsedTime(cached.Timestamp) < _ttl)
        {
            report = cached.Report;
            return true;
        }

        report = null!;
        return false;
    }

    private async Task<ReadinessReport> RunProbesAsync(CancellationToken ct)
    {
        var results = await Task.WhenAll(_probes.Select(async p =>
        {
            try
            {
                return (Probe: p, Healthy: (await p.CheckAsync(ct).ConfigureAwait(false)).IsHealthy);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // A probe must never throw, but a faulty one must read as unhealthy rather than 500 the endpoint.
                return (Probe: p, Healthy: false);
            }
        })).ConfigureAwait(false);

        var failed = new List<string>();
        var degraded = new List<string>();
        foreach (var (probe, healthy) in results)
        {
            snapshot.Set(probe.Name, healthy, probe.IsRequired);
            if (!healthy)
            {
                (probe.IsRequired ? failed : degraded).Add(probe.Name);
            }
        }

        return new ReadinessReport(failed, degraded);
    }
}
