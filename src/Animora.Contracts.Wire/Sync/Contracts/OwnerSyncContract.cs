using System.Text.Json;
namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Explicit allowed replication fields; identity and bookkeeping are carried by SyncRecord.</summary>
public sealed record OwnerSyncContract
{
    public string FullName { get; init; } = string.Empty;
    public string PhonePrimary { get; init; } = string.Empty;
    public string? PhoneSecondary { get; init; }
    public string? NationalId { get; init; }
    public string? Email { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public int Gender { get; init; }
    public string? Notes { get; init; }
    public DateTimeOffset RegisteredUtc { get; init; }
    public bool IsBlocked { get; init; }
    public string? BlockReason { get; init; }
}
