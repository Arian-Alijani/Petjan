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
    /// Runs a <b>time-boxed</b> level: <paramref name="concurrency"/> virtual users keep working for
    /// <paramref name="duration"/>, pausing a realistic think time between actions, then the measured latencies are
    /// summarised. Time-boxing (instead of a fixed request budget) is what makes a 5-user level and a 150-user level
    /// comparable, and is the only way to model a user who is <i>not</i> hammering the server.
    /// </summary>
    public static async Task<ProfileResult> RunForAsync(
        string name,
        int concurrency,
        TimeSpan duration,
        int thinkMinMs,
        int thinkMaxMs,
        Func<int, int, Task<Outcome>> operation,
        CancellationToken ct = default)
    {
        var latencies = new List<double>(concurrency * 8);
        int unexpected = 0, busy = 0, next = -1;
        var sync = new object();
        var wall = Stopwatch.StartNew();

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(duration);
        var token = stop.Token;

        Task[] workers = Enumerable.Range(0, concurrency).Select(worker => Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                int iteration = Interlocked.Increment(ref next);

                long start = Stopwatch.GetTimestamp();
                Outcome outcome;
                try
                {
                    outcome = await operation(worker, iteration).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
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

                // Think time is measured OUTSIDE the latency window: it models the clerk, not the server.
                try
                {
                    await Task.Delay(ThinkDelay(thinkMinMs, thinkMaxMs), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        })).ToArray();

        await Task.WhenAll(workers).ConfigureAwait(false);
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

    /// <summary>
    /// One human pause between two actions: log-uniform between <paramref name="minMs"/> and
    /// <paramref name="maxMs"/>, so most pauses are short and long ones are rare — the shape of real
    /// "read the screen, then act" behaviour rather than a uniform robot delay.
    /// </summary>
    public static int ThinkDelay(int minMs, int maxMs)
    {
        var lo = Math.Max(0, minMs);
        var hi = Math.Max(lo, maxMs);
        if (hi == lo)
        {
            return lo;
        }

        double u = Random.Shared.NextDouble();
        return (int)Math.Round(lo * Math.Pow((double)hi / Math.Max(lo, 1), u));
    }

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
