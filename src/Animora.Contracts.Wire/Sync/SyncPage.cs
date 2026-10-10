using System.Text.Json;
namespace Animora.Contracts.Wire.Sync;

public sealed record SyncPage(string BatchId, int PageOrdinal, int PageCount, IReadOnlyList<SyncRecord> Records, string PageHash);
