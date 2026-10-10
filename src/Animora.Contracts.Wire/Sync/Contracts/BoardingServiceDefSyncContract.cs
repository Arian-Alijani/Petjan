using System.Text.Json;
namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Explicit allowed replication fields; identity and bookkeeping are carried by SyncRecord.</summary>
public sealed record BoardingServiceDefSyncContract
{
    public string Code { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public JsonElement? UnitPrice { get; init; }
    public int Billing { get; init; }
    public bool IsActive { get; init; }
}
