using System.Text.Json;
namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Explicit allowed replication fields; identity and bookkeeping are carried by SyncRecord.</summary>
public sealed record TreatmentOccurrenceSyncContract
{
    public string PlanId { get; init; } = string.Empty;
    public DateOnly DueDay { get; init; }
    public int SortOrder { get; init; }
    public int State { get; init; }
    public DateTimeOffset? DoneUtc { get; init; }
    public string? DoneByUserId { get; init; }
}
