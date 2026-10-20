using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>
/// SYN-01: one durable provisional-id to server-id mapping learned when a creating command settled. Keyed by (tenant, local id) so a
/// repeat learn is an idempotent overwrite. <see cref="LocalOnlyAttribute">Local-only</see>, never replicated; pruned by retention
/// (<c>ILocalStoreMaintenance.CompactAsync</c>) long after every dependent has settled.
/// </summary>
[LocalOnly]
public sealed class CommandIdRemap
{
    private CommandIdRemap() { }

    /// <summary>Owning tenant (explicit predicate in every query: LocalOnly rows carry no global filter).</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>The client-generated provisional id.</summary>
    public string LocalId { get; private set; } = string.Empty;

    /// <summary>The server-allocated id it became.</summary>
    public string ServerId { get; private set; } = string.Empty;

    /// <summary>When the mapping was first learned (retention clock).</summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
