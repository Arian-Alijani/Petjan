using System.Text.Json;
namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Explicit allowed replication fields; identity and bookkeeping are carried by SyncRecord.</summary>
public sealed record RoleDefinitionSyncContract
{
    public int Role { get; init; }
    public string DisplayKey { get; init; } = string.Empty;
    public bool IsSystem { get; init; }
    public IReadOnlyList<string> PermissionCodes { get; init; } = [];
}
