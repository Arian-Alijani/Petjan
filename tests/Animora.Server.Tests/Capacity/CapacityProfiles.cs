using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Animora.Server.Tests.Capacity;

/// <summary>One measured load level of one profile (latencies in milliseconds).</summary>
public sealed record ProfileResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("concurrency")] int Concurrency,
    [property: JsonPropertyName("requests")] int Requests,
    [property: JsonPropertyName("throughput")] double Throughput,
    [property: JsonPropertyName("p50")] double P50,
    [property: JsonPropertyName("p95")] double P95,
    [property: JsonPropertyName("p99")] double P99,
    [property: JsonPropertyName("unexpectedFailures")] int UnexpectedFailures,
    [property: JsonPropertyName("expectedBusy")] int ExpectedBusy);

/// <summary>The planning-reference artifact (A-RULE 035: recorded, never a guaranteed user count).</summary>
public sealed record CapacityReport(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("cpus")] string Cpus,
    [property: JsonPropertyName("memory")] string Memory,
    [property: JsonPropertyName("postgresVersion")] string PostgresVersion,
    [property: JsonPropertyName("peakRssMiB")] double PeakRssMiB,
    [property: JsonPropertyName("peakPgConnections")] int PeakPgConnections,
    [property: JsonPropertyName("poolBound")] int PoolBound,
    [property: JsonPropertyName("statementTimeoutBoundMs")] double StatementTimeoutBoundMs,
    [property: JsonPropertyName("unexpected5xx")] int Unexpected5xx,
    [property: JsonPropertyName("readinessAtEnd")] string ReadinessAtEnd,
    [property: JsonPropertyName("profiles")] IReadOnlyList<ProfileResult> Profiles);

/// <summary>Closed-loop load generator: <c>concurrency</c> workers drain a shared request budget.</summary>
internal static class LoadRunner
{
    /// <summary>
    /// Runs <paramref name="total"/> operations across <paramref name="concurrency"/> workers. The operation receives
    /// (workerIndex, iteration) and returns <see cref="Outcome"/>; an exception is an unexpected failure.
    /// </summary>
    public static async Task<ProfileResult> RunAsync(
        string name, int concurrency, int total, Func<int, int, Task<Outcome>> operation)
    {
        var latencies = new List<double>(total);
        int unexpected = 0, busy = 0, next = -1;
        var sync = new object();
        var wall = Stopwatch.StartNew();

        Task[] workers = Enumerable.Range(0, concurrency).Select(worker => Task.Run(async () =>
        {
            while (true)
            {
                int iteration = Interlocked.Increment(ref next);
                if (iteration >= total)
                {
                    return;
                }

                long start = Stopwatch.GetTimestamp();
                Outcome outcome;
                try
                {
                    outcome = await operation(worker, iteration);
                }
                catch (Exception)
                {
                    outcome = Outcome.Unexpected;
                }

                double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                lock (sync)
                {
                    latencies.Add(ms);
                }

                if (outcome == Outcome.Unexpected)
                {
                    Interlocked.Increment(ref unexpected);
                }
                else if (outcome == Outcome.ExpectedBusy)
                {
                    Interlocked.Increment(ref busy);
                }
            }
        })).ToArray();

        await Task.WhenAll(workers);
        wall.Stop();

        latencies.Sort();
        return new ProfileResult(
            name,
            concurrency,
            latencies.Count,
            latencies.Count / Math.Max(wall.Elapsed.TotalSeconds, 0.001),
            Percentile(latencies, 0.50),
            Percentile(latencies, 0.95),
            Percentile(latencies, 0.99),
            unexpected,
            busy);
    }

    internal static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        int index = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }
}

/// <summary>Result of one load operation.</summary>
internal enum Outcome
{
    Ok,

    /// <summary>A documented back-pressure answer (e.g. 503 + Retry-After for an over-age snapshot): not a defect.</summary>
    ExpectedBusy,

    Unexpected,
}

/// <summary>Samples this process' working set and the PostgreSQL connections that belong to the harness.</summary>
internal sealed class ResourceSampler : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private long _peakRssBytes;
    private int _peakConnections;
    private int _disposed;

    public ResourceSampler(string adminConnection, string applicationNamePrefix)
    {
        _loop = Task.Run(async () =>
        {
            await using var conn = new Npgsql.NpgsqlConnection(adminConnection);
            await conn.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND application_name LIKE @p", conn);
            cmd.Parameters.AddWithValue("p", applicationNamePrefix + "%");
            using Process self = Process.GetCurrentProcess();
            while (!_stop.IsCancellationRequested)
            {
                self.Refresh();
                InterlockedMax(ref _peakRssBytes, self.WorkingSet64);
                int connections = (int)(long)(await cmd.ExecuteScalarAsync())!;
                int seen;
                while (connections > (seen = Volatile.Read(ref _peakConnections)))
                {
                    if (Interlocked.CompareExchange(ref _peakConnections, connections, seen) == seen)
                    {
                        break;
                    }
                }

                try
                {
                    await Task.Delay(50, _stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        });
    }

    public double PeakRssMiB => _peakRssBytes / 1048576.0;

    public int PeakConnections => Volatile.Read(ref _peakConnections);

    private static void InterlockedMax(ref long target, long value)
    {
        long seen;
        while (value > (seen = Interlocked.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, seen) == seen)
            {
                return;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _stop.CancelAsync();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }

        _stop.Dispose();
    }
}
