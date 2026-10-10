using Animora.Contracts.Wire.Sync;

namespace Animora.Sync.Custom;

/// <summary>The three possible answers to opening a batch.</summary>
public enum OpenBatchKind
{
    /// <summary>A frozen immutable batch is ready to download.</summary>
    Ready = 0,

    /// <summary>The client already reflects the server watermark.</summary>
    Current = 1,

    /// <summary>The client checkpoint is unusable; a full resync is required.</summary>
    Resync = 2,
}

/// <summary>Local union over <c>OpenBatchResponse</c>: exactly one of the payload members is set, matching <see cref="Kind"/>.</summary>
public sealed record OpenBatchOutcome(OpenBatchKind Kind, BatchReady? Batch, long Watermark, string? Reason, long Epoch)
{
    /// <summary>A batch is ready for download.</summary>
    public static OpenBatchOutcome Ready(BatchReady batch) => new(OpenBatchKind.Ready, batch, batch.Watermark, null, batch.Epoch);

    /// <summary>Nothing newer than <paramref name="watermark"/> exists.</summary>
    public static OpenBatchOutcome Current(long watermark) => new(OpenBatchKind.Current, null, watermark, null, 0);

    /// <summary>The server requires a full resync for <paramref name="reason"/>.</summary>
    public static OpenBatchOutcome Resync(string reason, long epoch) => new(OpenBatchKind.Resync, null, 0, reason, epoch);
}
