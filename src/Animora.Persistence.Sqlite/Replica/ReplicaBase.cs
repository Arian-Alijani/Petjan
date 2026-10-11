using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Replica;

/// <summary>
/// G04: one row of the client's read replica of server state, keyed by tenant, local base generation and the wire
/// <c>(Entity, EntityId)</c>. Generic by design — the payload is the opaque wire JSON — so the replica never touches a
/// business table or the pending command overlay. A tombstone keeps <see cref="Deleted"/> = true and no payload.
/// <para>
/// Two generations can coexist only inside one apply transaction: a full resync (shadow) writes a <b>new</b> generation and
/// switches <see cref="ReplicaCheckpoint.Generation"/> atomically, so the active base is never cleared before its replacement exists.
/// </para>
/// </summary>
[LocalOnly]
public sealed class ReplicaBase
{
    private ReplicaBase() { }

    /// <summary>Owning tenant (explicit predicate: LocalOnly rows are not covered by the business tenant filter).</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>Local base generation this row belongs to; only the generation named by the checkpoint is active.</summary>
    public long Generation { get; private set; }

    /// <summary>Wire entity name (e.g. <c>Pet</c>).</summary>
    public string Entity { get; private set; } = string.Empty;

    /// <summary>Wire entity id.</summary>
    public string EntityId { get; private set; } = string.Empty;

    /// <summary>Opaque server version of the row, when the server supplied one.</summary>
    public string? Version { get; private set; }

    /// <summary>Wire payload JSON; <see langword="null"/> for a tombstone.</summary>
    public string? Payload { get; private set; }

    /// <summary>True when the server reported the row deleted.</summary>
    public bool Deleted { get; private set; }

    /// <summary>Server <c>UpdatedAt</c> of the row, when supplied.</summary>
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
}
