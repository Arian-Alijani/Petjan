using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Animora.LiveTest.Capacity;

/// <summary>
/// Closed-loop load primitives for the live capacity suite. These mirror the shapes used by the
/// in-tree CI capacity driver (tests/Animora.Server.Tests/Capacity/CapacityProfiles.cs) but carry
/// NO Npgsql/server dependency — this harness runs on the operator's machine over real HTTP, and
/// the server's hardware is observed out-of-band by the SSH RemoteMonitor (task T5), not via a
/// local pg_stat_activity connection.
/// </summary>

/// <summary>Outcome of one load operation.</summary>
public enum Outcome
{
    Ok,

    /// <summary>A documented back-pressure answer (503 + Retry-After, deferred settlement): not a defect.</summary>
    ExpectedBusy,

    Unexpected,
}

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

/// <summary>Closed-loop generator: <c>concurrency</c> virtual users drain a shared request budget.</summary>
public static class LoadRunner
{
    /// <summary>
    /// Runs <paramref name="total"/> operations across <paramref name="concurrency"/> workers. The operation
    /// receives (workerIndex, iteration) and returns an <see cref="Outcome"/>; an exception is an unexpected failure.
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

    public static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        int index = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }
}
