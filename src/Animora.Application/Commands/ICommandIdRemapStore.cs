namespace Animora.Application.Commands;

/// <summary>
/// Durable backing for the provisional (client-minted) id to server id map (SYN-01). The in-memory map in the sync layer is only an L1 cache:
/// a creator can settle, the app can restart, and the dependents still waiting in the journal must keep resolving to the SAME server ids.
/// Synchronous by design: rows are tiny, the table is bounded by retention, and the caller (the envelope rewrite) is synchronous.
/// </summary>
public interface ICommandIdRemapStore
{
    /// <summary>Idempotently records (or overwrites) <paramref name="localId"/> -&gt; <paramref name="serverId"/> for the tenant.</summary>
    void Upsert(string tenantId, string localId, string serverId, DateTimeOffset createdAtUtc);

    /// <summary>Every mapping stored for the tenant (hydrates the in-memory L1 cache once per tenant per process).</summary>
    IReadOnlyList<KeyValuePair<string, string>> LoadAll(string tenantId);
}
