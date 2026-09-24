using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Seed;

/// <summary>
/// Device-local record that one logical first-run seed block has been applied. The unique index on
/// <see cref="SeedKey"/> makes rerun, partial and concurrent seeding safe. Never replicated.
/// </summary>
[LocalOnly]
public sealed class SeedMarker : LocalOnlyEntity
{
    private SeedMarker()
    {
    }

    /// <summary>Stable key of the seed block, e.g. <c>"finance.chart"</c>.</summary>
    public string SeedKey { get; private set; } = string.Empty;

    /// <summary>Content version of the block at the time it was applied.</summary>
    public int SchemaVersion { get; private set; }

    internal static SeedMarker Create(string seedKey, int schemaVersion, DateTimeOffset nowUtc) => new()
    {
        Id = IdGenerator.NewId(nowUtc),
        SeedKey = seedKey,
        SchemaVersion = schemaVersion,
        CreatedAtUtc = nowUtc,
    };
}
