namespace Animora.Domain.Time;

/// <summary>
/// A rollback-resistant time source. Client wall-clock time is <b>never</b> a
/// synchronization or licensing authority (A-RULE 011): moving the OS clock backwards
/// must not extend an offline entitlement or rewind a sync cursor. Consumers are the
/// licensing/offline-capability paths (D11) and the sync scheduler (E07).
/// </summary>
/// <remarks>Implementations live in <c>Animora.Infrastructure.Local</c> (Phase A11).</remarks>
public interface IMonotonicClock
{
    /// <summary>
    /// Ticks from a process-local monotonic source (never affected by clock adjustments).
    /// Meaningful only as a difference between two readings, never as an absolute date.
    /// </summary>
    long ElapsedTicks { get; }

    /// <summary>
    /// The highest UTC instant ever observed by this device, persisted across restarts.
    /// Monotonic by contract: it never decreases, whatever the OS clock does.
    /// </summary>
    DateTimeOffset HighWaterUtc { get; }

    /// <summary>
    /// Feeds a trusted server instant in. Advances <see cref="HighWaterUtc"/> when
    /// <paramref name="serverUtc"/> is later than the current high-water value; an earlier
    /// value is ignored, never applied.
    /// </summary>
    void Observe(DateTimeOffset serverUtc);
}
