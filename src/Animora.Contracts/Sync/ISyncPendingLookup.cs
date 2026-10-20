namespace Animora.Contracts.Sync;

/// <summary>
/// Where one record stands relative to the server, as far as the local command queue knows. Absence of a mark
/// (<see cref="None"/>) means the record is applied on the server (or never had a queued write on this device).
/// </summary>
public enum SyncQueueMark
{
    /// <summary>No unsettled command for the record — nothing to show.</summary>
    None,

    /// <summary>Written locally only: waiting in the send queue; it is applied on the server after the next connection.</summary>
    Queued,

    /// <summary>The server refused or conflicted with a write of the record (or retries ran out); an operator must review it.</summary>
    NeedsAttention,
}

/// <summary>
/// An immutable, point-in-time index of every record that still has an unsettled command, keyed by its aggregate
/// (<c>Type</c> + <c>Id</c>). Built by ONE query per refresh; every list row then resolves its mark with an O(1)
/// dictionary lookup — never a per-row query against the journal.
/// </summary>
public sealed class SyncQueueSnapshot
{
    /// <summary>The snapshot with no marks (offline-free device, or the queue could not be read).</summary>
    public static readonly SyncQueueSnapshot Empty = new(new Dictionary<string, SyncQueueMark>(0, StringComparer.Ordinal));

    private readonly Dictionary<string, SyncQueueMark> _marks;

    public SyncQueueSnapshot(Dictionary<string, SyncQueueMark> marks)
    {
        _marks = marks ?? throw new ArgumentNullException(nameof(marks));
    }

    /// <summary>Number of records that carry a mark.</summary>
    public int Count => _marks.Count;

    /// <summary>Builds the lookup key of an aggregate (the shape used by <see cref="MarkFor"/>).</summary>
    public static string KeyOf(string aggregateType, string aggregateId) => aggregateType + "\u001f" + aggregateId;

    /// <summary>The mark of one record, or <see cref="SyncQueueMark.None"/> when it has no unsettled command.</summary>
    public SyncQueueMark MarkFor(string? aggregateType, string? aggregateId) =>
        string.IsNullOrEmpty(aggregateType) || string.IsNullOrEmpty(aggregateId)
            ? SyncQueueMark.None
            : _marks.TryGetValue(KeyOf(aggregateType, aggregateId), out var mark) ? mark : SyncQueueMark.None;
}

/// <summary>
/// Read seam for the «هنوز روی سرور اعمال نشده» marks shown in the histories (SYN-02): which records are only
/// queued locally and which were refused by the server. One call returns the whole index, so a screen pays for a
/// single read per refresh regardless of how many rows it shows.
/// </summary>
public interface ISyncPendingLookup
{
    /// <summary>Reads the current queue index. Never throws for a store failure — returns <see cref="SyncQueueSnapshot.Empty"/>.</summary>
    Task<SyncQueueSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}
