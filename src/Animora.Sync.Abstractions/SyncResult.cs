namespace Animora.Sync.Abstractions;

/// <summary>The outcome of a pull: what happened, the watermark now reflected, counts and completion time.</summary>
/// <param name="Outcome">The classification of this pull.</param>
/// <param name="Watermark">The server ChangeSequence high-water the local base now reflects; null when nothing applied/deferred.</param>
/// <param name="BatchesApplied">Immutable batches applied (>= 0).</param>
/// <param name="RecordsApplied">Records applied across all batches (>= 0).</param>
/// <param name="CompletedAtUtc">When the pull completed (injected clock).</param>
/// <param name="Detail">Optional human-readable note (offline reason / resync reason).</param>
public sealed record SyncResult(
    SyncOutcome Outcome,
    long? Watermark,
    int BatchesApplied,
    int RecordsApplied,
    DateTimeOffset CompletedAtUtc,
    string? Detail)
{
    /// <summary>Offline / unconfigured: a success carrying no watermark.</summary>
    public static SyncResult Deferred(DateTimeOffset nowUtc, string? detail = null) =>
        new(SyncOutcome.Deferred, null, 0, 0, nowUtc, detail);

    /// <summary>Nothing to download; the local base already reflects <paramref name="watermark"/>.</summary>
    public static SyncResult UpToDate(long watermark, DateTimeOffset nowUtc) =>
        new(SyncOutcome.UpToDate, watermark, 0, 0, nowUtc, null);

    /// <summary>Batches downloaded, verified and applied; records and watermark advanced.</summary>
    public static SyncResult Applied(long watermark, int batches, int records, DateTimeOffset nowUtc) =>
        new(SyncOutcome.Applied, watermark, batches, records, nowUtc, null);

    /// <summary>The server requires a full resync; <paramref name="reason"/> is carried in Detail.</summary>
    public static SyncResult ResyncRequired(string reason, DateTimeOffset nowUtc) =>
        new(SyncOutcome.ResyncRequired, null, 0, 0, nowUtc, reason);
}
