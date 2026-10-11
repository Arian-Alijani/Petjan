using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Replica;

/// <summary>
/// G04: the replication position the active <see cref="ReplicaBase"/> generation reflects, per tenant and sync scope.
/// Written only in the same transaction that changes the base, so base and checkpoint can never disagree after a crash.
/// </summary>
[LocalOnly]
public sealed class ReplicaCheckpoint
{
    private ReplicaCheckpoint() { }

    /// <summary>Owning tenant.</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>Sync scope name.</summary>
    public string Scope { get; private set; } = string.Empty;

    /// <summary>Server epoch the base reflects.</summary>
    public long Epoch { get; private set; }

    /// <summary>Server ChangeSequence watermark the base reflects.</summary>
    public long Watermark { get; private set; }

    /// <summary>Local active base generation (<see cref="ReplicaBase.Generation"/>).</summary>
    public long Generation { get; private set; }

    /// <summary>Server-side generation of the last applied batch (informational).</summary>
    public long ServerGeneration { get; private set; }
}
