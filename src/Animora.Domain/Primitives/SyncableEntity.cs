namespace Animora.Domain.Primitives;

/// <summary>
/// Base class for every entity that participates in generic record replication
/// (<c>CONVENTIONS.md</c> §6 item 3 — the "syncable shape"):
/// <c>Id</c> | <c>TenantId</c> | <c>UpdatedAt</c> | <c>Version</c> | <c>Deleted</c>.
/// </summary>
/// <remarks>
/// No EF Core attribute appears here — mapping lives entirely in the
/// <c>&lt;Entity&gt;Configuration</c> classes (Phase A09). The protected parameterless
/// constructor exists solely so EF Core can materialize instances.
/// </remarks>
public abstract class SyncableEntity
{
    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    protected SyncableEntity()
    {
    }

    /// <summary>
    /// Stable primary key: a 26-character Crockford base32 ULID produced by
    /// <see cref="IdGenerator"/>. Never a <c>Guid</c> (CONVENTIONS.md §4).
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Owning tenant. Every replicated row is tenant-scoped.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Transport bookkeeping only (A-RULE 009): the basis of the delta-sync cursor.
    /// Server-stamped on write. It is <b>never</b> a business sequence, never an
    /// audit timestamp and never an ordering authority for domain logic.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Transport bookkeeping only (A-RULE 010): an opaque, server-issued concurrency
    /// token. <c>null</c> until the row has been written by the server at least once.
    /// It is <b>never</b> a business sequence or version number the domain may reason about;
    /// a mismatch only <i>detects</i> a conflict — domain policy decides (A-RULE 004).
    /// </summary>
    public string? Version { get; set; }

    /// <summary>Soft-delete tombstone; replicated so peers can converge on removal.</summary>
    public bool Deleted { get; set; }
}
