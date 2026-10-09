using Animora.Domain.Results;

namespace Animora.Persistence.PostgreSql.Transactions;

/// <summary>
/// The seam <c>ServerUnitOfWork.ExecuteAsync</c> delegates its transactional work to. Extracting it from
/// the unit of work lets the serialization/deadlock retry state machine (<see cref="RetryPolicy"/>) be unit
/// tested without a live PostgreSQL connection. The production implementation
/// (<c>ServerTransactionRunner</c>) wraps an EF Core execution strategy around
/// begin → tenant-session-init → work → commit/rollback.
/// </summary>
internal interface ITransactionRunner
{
    /// <summary>
    /// Runs <paramref name="work"/> inside a retrying transaction, returning the work's own failure
    /// <see cref="Result{T}"/> without throwing. Cancellation is never swallowed.
    /// </summary>
    Task<Result<T>> RunAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct);
}

/// <summary>
/// Pure, dependency-free retry core shared by the real runner and its unit tests. It classifies a thrown
/// PostgreSQL error as transient (serialization <c>40001</c> / deadlock <c>40P01</c>) or not, applies a
/// jittered back-off between attempts, caps the attempt count at <see cref="MaxAttempts"/>, and never
/// retries (or swallows) a cancellation.
/// </summary>
internal static class RetryPolicy
{
    /// <summary>PostgreSQL SQLSTATE for serialization_failure.</summary>
    public const string SerializationFailure = "40001";

    /// <summary>PostgreSQL SQLSTATE for deadlock_detected.</summary>
    public const string DeadlockDetected = "40P01";

    /// <summary>PostgreSQL SQLSTATE for unique_violation (never retried).</summary>
    public const string UniqueViolation = "23505";

    /// <summary>Total attempts made before the last transient failure is surfaced.</summary>
    public const int MaxAttempts = 3;

    private const double MinBackoffMs = 50;
    private const double MaxBackoffMs = 250;

    /// <summary><see langword="true"/> for the serialization/deadlock SQLSTATEs that are safe to retry.</summary>
    public static bool IsTransient(string? sqlState) =>
        sqlState is SerializationFailure or DeadlockDetected;

    /// <summary>
    /// A jittered back-off for the given 1-based attempt: a uniform draw in
    /// <c>[50&#160;ms, 250&#160;ms]</c> using <paramref name="rng"/> (a value in <c>[0, 1)</c>). The attempt
    /// number is accepted so a future phase can widen the window per attempt without changing callers.
    /// </summary>
    public static TimeSpan BackoffFor(int attempt, Func<double> rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        double unit = Math.Clamp(rng(), 0d, 1d);
        double ms = MinBackoffMs + (unit * (MaxBackoffMs - MinBackoffMs));
        return TimeSpan.FromMilliseconds(ms);
    }

    /// <summary>
    /// Drives <paramref name="attempt"/> (receiving the 1-based attempt number) up to
    /// <see cref="MaxAttempts"/> times. A thrown exception for which <paramref name="isTransient"/> returns
    /// <see langword="true"/> is retried after <see cref="BackoffFor"/>; any other exception — including an
    /// <see cref="OperationCanceledException"/> — propagates immediately. After the final transient failure
    /// the exception is rethrown.
    /// </summary>
    /// <param name="attempt">The transactional attempt; returns the work's <see cref="Result{T}"/>.</param>
    /// <param name="isTransient">Classifies a thrown exception as retryable.</param>
    /// <param name="rng">Uniform source in <c>[0, 1)</c> for back-off jitter.</param>
    /// <param name="delay">Awaitable delay (real <c>Task.Delay</c> in production, no-op in tests).</param>
    /// <param name="ct">Cancellation token; honored and never swallowed.</param>
    public static async Task<Result<T>> ExecuteAsync<T>(
        Func<int, CancellationToken, Task<Result<T>>> attempt,
        Func<Exception, bool> isTransient,
        Func<double> rng,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(isTransient);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(delay);

        for (int number = 1; ; number++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await attempt(number, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is control flow, never a transient fault: propagate.
                throw;
            }
            catch (Exception ex) when (number < MaxAttempts && isTransient(ex))
            {
                await delay(BackoffFor(number, rng), ct).ConfigureAwait(false);
            }
        }
    }
}
