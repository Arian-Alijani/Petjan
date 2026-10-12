namespace Animora.Client.Services.Sync;

/// <summary>Cadence and scope configuration for the client state-pull scheduler; bound from <see cref="SectionName"/>.</summary>
public sealed class SyncScheduleOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Animora:Sync";

    /// <summary>When false the scheduler never starts (on-demand pulls still work).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Base wait between two scheduled pulls. Short on purpose: a healthy pull is two cheap requests (manifest + an
    /// <c>UpToDate</c> batch answer), and this cadence is what makes another user's change appear on every device within
    /// about half a minute. Faults still back off exponentially up to <see cref="MaxBackoff"/>.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Lower bound for any computed wait (guards against a misconfigured tiny interval).</summary>
    public TimeSpan MinInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Symmetric jitter applied to every wait, as a fraction (0.2 = ±20%).</summary>
    public double JitterFraction { get; set; } = 0.2;

    /// <summary>
    /// Short re-check wait used while pulls are <c>Deferred</c> (no online session yet) so the first pull starts promptly
    /// after sign-in instead of waiting a full <see cref="Interval"/>. Still floored at 1 second.
    /// </summary>
    public TimeSpan DeferredRetryInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Upper bound of the exponential backoff after network/busy faults.</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Scope names pulled on every cycle.</summary>
    public IReadOnlyList<string> Scopes { get; set; } = ["default"];
}
