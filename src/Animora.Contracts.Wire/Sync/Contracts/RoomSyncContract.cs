using System.Text.Json;
namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Explicit allowed replication fields; identity and bookkeeping are carried by SyncRecord.</summary>
public sealed record RoomSyncContract
{
    public string Code { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public int Type { get; init; }
    public int Capacity { get; init; }
    public int Status { get; init; }
    public string? Notes { get; init; }
}
