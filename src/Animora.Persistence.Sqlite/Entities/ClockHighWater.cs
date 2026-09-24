using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>Highest UTC time this installation has observed (clock-rollback guard); single row. Never replicated.</summary>
[LocalOnly]
public sealed class ClockHighWater
{
    /// <summary>Fixed key of the single row.</summary>
    public const int SingletonId = 1;

    private ClockHighWater()
    {
    }

    public int Id { get; private set; } = SingletonId;
    public DateTimeOffset HighWaterUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    internal static ClockHighWater Create(DateTimeOffset nowUtc) =>
        new() { HighWaterUtc = nowUtc, UpdatedAtUtc = nowUtc };

    /// <summary>Moves the mark forward only; an earlier observation never lowers it.</summary>
    internal void Observe(DateTimeOffset observedUtc, DateTimeOffset nowUtc)
    {
        if (observedUtc > HighWaterUtc) HighWaterUtc = observedUtc;
        UpdatedAtUtc = nowUtc;
    }
}
