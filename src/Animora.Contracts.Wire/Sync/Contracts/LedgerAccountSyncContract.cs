using System.Text.Json;
namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Explicit allowed replication fields; identity and bookkeeping are carried by SyncRecord.</summary>
public sealed record LedgerAccountSyncContract
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Type { get; init; }
    public string? ParentAccountId { get; init; }
    public bool IsPostable { get; init; }
    public bool IsActive { get; init; }
    public string? Description { get; init; }
}
