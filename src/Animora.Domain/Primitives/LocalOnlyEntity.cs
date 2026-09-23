namespace Animora.Domain.Primitives;

/// <summary>
/// Base class for entities that exist only on this device and are <b>never</b>
/// replicated (CONVENTIONS.md §6 item 3). Types deriving from this base are
/// additionally marked with <see cref="LocalOnlyAttribute"/> so the model builder
/// (A09) and the sync scope (E03) can exclude them mechanically.
/// </summary>
public abstract class LocalOnlyEntity
{
    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    protected LocalOnlyEntity()
    {
    }

    /// <summary>26-character ULID produced by <see cref="IdGenerator"/>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// When the local row was created, in UTC. Local diagnostic/housekeeping data only;
    /// it carries no cross-device meaning and is never a sync cursor.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; }
}
