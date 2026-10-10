using System.Text.Json;
namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Explicit allowed replication fields; identity and bookkeeping are carried by SyncRecord.</summary>
public sealed record MedicalHistoryEntrySyncContract
{
    public string PetId { get; init; } = string.Empty;
    public int Kind { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public DateTimeOffset OccurredUtc { get; init; }
    public string? SourceEntityId { get; init; }
    public string? SourceEntityType { get; init; }
    public string RecordedByUserId { get; init; } = string.Empty;
}
