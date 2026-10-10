using System.Text.Json;
namespace Animora.Contracts.Wire.Sync;

public sealed record BatchReady(string BatchId, long Epoch, long Generation, long FromSequenceExclusive, long Watermark, int PageCount, string ContentHash);
public sealed record UpToDate(long Watermark);
public sealed record ResyncRequired(string Reason, long Epoch);
public sealed record OpenBatchResponse(string Kind, BatchReady? Batch = null, UpToDate? Current = null, ResyncRequired? Resync = null);
