using System.Text.Json;
namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Explicit allowed replication fields; identity and bookkeeping are carried by SyncRecord.</summary>
public sealed record WeightEntrySyncContract
{
    public string PetId { get; init; } = string.Empty;
    public decimal WeightKg { get; init; }
    public DateTimeOffset MeasuredUtc { get; init; }
    public string? Note { get; init; }
    public string RecordedByUserId { get; init; } = string.Empty;
    public string? CorrectsEntryId { get; init; }
}
