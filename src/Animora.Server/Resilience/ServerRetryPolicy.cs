namespace Animora.Server.Resilience;

internal static class ServerRetryPolicy
{
    public const int MaxAttempts = 8;
    public static readonly TimeSpan Cap = TimeSpan.FromMinutes(15);
    public static TimeSpan NextDelay(int attempt, Func<double> random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (attempt < 1) throw new ArgumentOutOfRangeException(nameof(attempt));
        double value = random();
        if (!double.IsFinite(value) || value < 0 || value >= 1) throw new ArgumentOutOfRangeException(nameof(random));
        double bound = Math.Min(Cap.TotalSeconds, 2 * Math.Pow(2, Math.Min(attempt - 1, 30)));
        return TimeSpan.FromSeconds(value * bound);
    }
}
