using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>
/// C11: a <see cref="LocalOnlyAttribute">local-only</see> marker of when this device last completed a
/// successful sync pass, per tenant and sync <see cref="Scope"/>. It backs
/// <c>LocalSyncStatusService.LastSyncedAt</c> so the topbar can show "last synced" with no server.
/// <para>
/// Deliberately minimal: today the only writer is <c>LocalCommandProcessor</c>'s no-op pass over the
/// <c>NullCommandTransport</c>. <b>E03/E04 extend this row</b> (real cursor tokens, per-collection
/// watermarks) when the sync engine lands — the registration and this table stay; only the internals grow.
/// </para>
/// </summary>
[LocalOnly]
public sealed class SyncCursor
{
    /// <summary>Default scope for the single whole-store cursor before per-collection sync exists.</summary>
    public const string DefaultScope = "default";

    private SyncCursor() { }

    /// <summary>ULID primary key.</summary>
    public string Id { get; private set; } = string.Empty;

    /// <summary>Owning tenant.</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>Sync scope (one cursor per tenant + scope). <see cref="DefaultScope"/> until E03 splits it.</summary>
    public string Scope { get; private set; } = DefaultScope;

    /// <summary>When the last successful sync pass settled, or <see langword="null"/> when none has yet.</summary>
    public DateTimeOffset? LastSyncedAtUtc { get; private set; }

    /// <summary>Creates a fresh cursor for a tenant/scope with no successful sync recorded yet.</summary>
    public static SyncCursor Create(string tenantId, string scope = DefaultScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        return new()
        {
            Id = IdGenerator.NewId(),
            TenantId = tenantId,
            Scope = scope,
            LastSyncedAtUtc = null,
        };
    }

    /// <summary>Records that a sync pass completed successfully at <paramref name="whenUtc"/>.</summary>
    public void MarkSynced(DateTimeOffset whenUtc) => LastSyncedAtUtc = whenUtc;
}
