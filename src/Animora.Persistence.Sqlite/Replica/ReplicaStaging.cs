using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Replica;

/// <summary>
/// G04: scratch rows of the in-flight batch only. Pages are streamed here one at a time (bounded memory); the rows reach
/// <see cref="ReplicaBase"/> only in the single commit transaction, and are always cleared for the batch afterwards or on abort.
/// </summary>
[LocalOnly]
public sealed class ReplicaStaging
{
    private ReplicaStaging() { }

    /// <summary>Owning tenant.</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>Server batch id the row belongs to.</summary>
    public string BatchId { get; private set; } = string.Empty;

    /// <summary>Wire entity name.</summary>
    public string Entity { get; private set; } = string.Empty;

    /// <summary>Wire entity id.</summary>
    public string EntityId { get; private set; } = string.Empty;

    /// <summary>Ordinal of the page that last carried this row.</summary>
    public int PageOrdinal { get; private set; }

    /// <summary>Opaque server version.</summary>
    public string? Version { get; private set; }

    /// <summary>Wire payload JSON; <see langword="null"/> for a tombstone.</summary>
    public string? Payload { get; private set; }

    /// <summary>True for a delete operation or a row flagged deleted.</summary>
    public bool Deleted { get; private set; }

    /// <summary>Server <c>UpdatedAt</c>, when supplied.</summary>
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
}
