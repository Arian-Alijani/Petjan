using System.Text.Json;
namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Explicit allowed replication fields; identity and bookkeeping are carried by SyncRecord.</summary>
public sealed record AccountingPeriodSyncContract
{
    public string PeriodCode { get; init; } = string.Empty;
    public DateTimeOffset StartUtc { get; init; }
    public DateTimeOffset EndUtc { get; init; }
    public int State { get; init; }
    public DateTimeOffset? ClosedUtc { get; init; }
    public string? ClosedByUserId { get; init; }
    public string? ClosingEntryId { get; init; }
    public string? Note { get; init; }
}
