using System.Text.Json;
using Animora.Contracts.Wire.Sync;

namespace Animora.LiveTest.Client;

/// <summary>One replicated row as last seen by a virtual client.</summary>
public sealed record ReplicaRow(string? Version, DateTimeOffset? UpdatedAt, JsonElement? Payload);

/// <summary>
/// Throwaway in-memory replica of what the server's state feed has delivered to ONE virtual client:
/// entity → id → row. It exists only so scenarios can assert convergence between clients
/// ("A wrote it, B pulled it, both hold the same server version"). It is deliberately not the production
/// SQLite replica: the live-test tool measures the server, not the client store.
/// </summary>
public sealed class ReplicaView
{
    private readonly Dictionary<string, Dictionary<string, ReplicaRow>> _rows = new(StringComparer.Ordinal);

    /// <summary>Highest feed sequence applied so far (0 = nothing pulled yet).</summary>
    public long Watermark { get; private set; }

    /// <summary>Feed epoch the watermark belongs to.</summary>
    public long Epoch { get; private set; }

    /// <summary>True once a full snapshot has been applied (incremental pulls are valid only after that).</summary>
    public bool HasSnapshot { get; private set; }

    /// <summary>Total rows across all entities.</summary>
    public int TotalRows => _rows.Values.Sum(e => e.Count);

    /// <summary>Entity names currently present, sorted.</summary>
    public IReadOnlyList<string> Entities => _rows.Keys.Order(StringComparer.Ordinal).ToArray();

    /// <summary>Number of rows of <paramref name="entity"/>.</summary>
    public int Count(string entity) => _rows.TryGetValue(entity, out var rows) ? rows.Count : 0;

    /// <summary>Looks one row up.</summary>
    public bool TryGet(string entity, string id, out ReplicaRow row)
    {
        row = null!;
        return _rows.TryGetValue(entity, out var rows) && rows.TryGetValue(id, out row!);
    }

    /// <summary>Rows of an entity (empty when none).</summary>
    public IEnumerable<KeyValuePair<string, ReplicaRow>> Rows(string entity) =>
        _rows.TryGetValue(entity, out var rows) ? rows : Enumerable.Empty<KeyValuePair<string, ReplicaRow>>();

    /// <summary>Finds the first row of <paramref name="entity"/> matching the predicate.</summary>
    public (string Id, ReplicaRow Row)? Find(string entity, Func<string, ReplicaRow, bool> predicate)
    {
        foreach (var (id, row) in Rows(entity))
            if (predicate(id, row)) return (id, row);
        return null;
    }

    internal void Reset()
    {
        _rows.Clear();
        Watermark = 0;
        HasSnapshot = false;
    }

    internal void Apply(IEnumerable<SyncRecord> records)
    {
        foreach (var r in records)
        {
            if (r.Deleted)
            {
                if (_rows.TryGetValue(r.Entity, out var existing)) existing.Remove(r.EntityId);
                continue;
            }

            if (!_rows.TryGetValue(r.Entity, out var rows))
                _rows[r.Entity] = rows = new Dictionary<string, ReplicaRow>(StringComparer.Ordinal);
            rows[r.EntityId] = new ReplicaRow(r.Version, r.UpdatedAt, r.Payload?.Clone());
        }
    }

    internal void Commit(long epoch, long watermark, bool snapshot)
    {
        Epoch = epoch;
        Watermark = watermark;
        if (snapshot) HasSnapshot = true;
    }
}
