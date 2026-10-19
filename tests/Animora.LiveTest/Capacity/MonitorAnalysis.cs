using System.Diagnostics;

namespace Animora.LiveTest.Capacity;

/// <summary>
/// Probes the anonymous <c>/health/ready</c> endpoint on a fixed interval. Works without SSH, so even a client-only
/// capacity run can tell the operator when the server stopped answering "ready" under load.
/// </summary>
public sealed class HealthProbe : IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly TimeSpan _interval;
    private readonly object _gate = new();
    private readonly List<HealthSample> _samples = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public HealthProbe(string baseUrl, TimeSpan interval)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl, UriKind.Absolute), Timeout = TimeSpan.FromSeconds(10) };
        _interval = interval;
        _loop = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var sw = Stopwatch.StartNew();
            int? status = null;
            try
            {
                using var response = await _http.GetAsync("health/ready", _stop.Token).ConfigureAwait(false);
                status = (int)response.StatusCode;
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // unreachable / timeout: recorded as status null
            }

            lock (_gate) _samples.Add(new HealthSample(DateTimeOffset.UtcNow, status, sw.Elapsed.TotalMilliseconds));
            try { await Task.Delay(_interval, _stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    public IReadOnlyList<HealthSample> Snapshot()
    {
        lock (_gate) return _samples.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try { await _loop.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
        catch (Exception) { /* best effort */ }
        _http.Dispose();
        _stop.Dispose();
    }
}

/// <summary>Pure window aggregation of monitor samples (unit-testable, no I/O).</summary>
public static class MonitorAnalysis
{
    /// <summary>Peaks/minimums of everything sampled inside [from, to].</summary>
    public static MonitorSummary Summarize(IReadOnlyList<ServerSample> server, IReadOnlyList<HealthSample> health,
        DateTimeOffset from, DateTimeOffset to, int? pgMaxConnections)
    {
        var s = server.Where(x => x.At >= from && x.At <= to).ToArray();
        var h = health.Where(x => x.At >= from && x.At <= to).ToArray();

        var healthP95 = h.Length == 0 ? 0 : LoadRunner.Percentile(h.Select(x => x.LatencyMs).Order().ToArray(), 0.95);
        var healthNotReady = h.Count(x => !x.Ready);

        if (s.Length == 0)
        {
            return MonitorSummary.Empty with { HealthProbes = h.Length, HealthNotReady = healthNotReady, HealthP95Ms = healthP95 };
        }

        var pg = s.Where(x => x.PgTotal.HasValue).Select(x => x.PgTotal!.Value).ToArray();
        var rss = s.Where(x => x.ServiceRssMb.HasValue).Select(x => x.ServiceRssMb!.Value).ToArray();
        return new MonitorSummary(
            ServerMetricsAvailable: true,
            Samples: s.Length,
            CpuPeakPercent: s.Max(x => x.CpuPercent),
            CpuAveragePercent: s.Average(x => x.CpuPercent),
            Load1Peak: s.Max(x => x.Load1),
            MemUsedPeakMb: s.Max(x => x.MemUsedMb),
            MemAvailableMinMb: s.Min(x => x.MemAvailableMb),
            MemTotalMb: s.Max(x => x.MemTotalMb),
            SwapUsedPeakMb: s.Max(x => x.SwapUsedMb),
            EstablishedPeak: s.Max(x => x.Established),
            ServiceRssPeakMb: rss.Length == 0 ? null : rss.Max(),
            ServiceDownSamples: s.Count(x => x.ServiceActive == false),
            PgConnectionsPeak: pg.Length == 0 ? null : pg.Max(),
            PgMaxConnections: pgMaxConnections,
            HealthProbes: h.Length,
            HealthNotReady: healthNotReady,
            HealthP95Ms: healthP95);
    }
}
