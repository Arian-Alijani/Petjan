using System.Text.Json;
namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Explicit allowed replication fields; identity and bookkeeping are carried by SyncRecord.</summary>
public sealed record DocumentReferenceSyncContract
{
    public string? OwnerId { get; init; }
    public string? PetId { get; init; }
    public string? VisitId { get; init; }
    public string Title { get; init; } = string.Empty;
    public int Kind { get; init; }
    public string FileId { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string ContentHashSha256 { get; init; } = string.Empty;
    public DateTimeOffset UploadedUtc { get; init; }
    public string UploadedByUserId { get; init; } = string.Empty;
}
