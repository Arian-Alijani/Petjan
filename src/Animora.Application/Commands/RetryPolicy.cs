namespace Animora.Application.Commands;

/// <summary>
/// The single retry-backoff policy for the local command pipeline and the outbox relay (ARCHITECTURE.md §12.3):
/// exponential backoff with jitter. Delay = <c>min(2^attempts seconds, 15 min)</c>, then ±20% jitter so a mass
/// reconnect never synchronizes into a storm (Scenario I).
/// </summary>
public static class RetryPolicy
{
    /// <summary>Attempts before a command or outbox message is moved to needs-attention / dead-letter.</summary>
    public const int MaxAttempts = 10;

    private static readonly TimeSpan Cap = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The un-jittered exponential delay for <paramref name="attempts"/> completed attempts (1 → 2 s, 2 → 4 s, …).
    /// The <see cref="Cap"/> bounds it at 15 minutes.
    /// </summary>
    public static TimeSpan NextDelay(int attempts)
    {
        var exponent = Math.Clamp(attempts, 1, MaxAttempts); // bound to the real attempt budget; Cap already dominates.
        var seconds = Math.Pow(2, exponent);
        return seconds >= Cap.TotalSeconds ? Cap : TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// The backoff to apply, with ±20% jitter. <paramref name="jitterSample"/> is a caller-supplied value in
    /// <c>[0,1)</c> so the caller controls determinism (tests pass a fixed value; production passes a random one).
    /// </summary>
    public static TimeSpan NextDelay(int attempts, double jitterSample)
    {
        var baseDelay = NextDelay(attempts);
        var clamped = Math.Clamp(jitterSample, 0d, 0.999999d);
        var factor = 1d + ((clamped * 0.4d) - 0.2d); // [-0.2, +0.2)
        return TimeSpan.FromSeconds(baseDelay.TotalSeconds * factor);
    }

    /// <summary>
    /// Whether <paramref name="attempts"/> completed attempts have exhausted the retry budget. Reaching
    /// <see cref="MaxAttempts"/> spends the budget: the command moves to needs-attention and an outbox message
    /// is dead-lettered, each with its last error preserved.
    /// </summary>
    public static bool IsExhausted(int attempts) => attempts >= MaxAttempts;
}
