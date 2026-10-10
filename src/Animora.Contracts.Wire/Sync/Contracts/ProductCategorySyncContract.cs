using System.Text.Json;
namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Explicit allowed replication fields; identity and bookkeeping are carried by SyncRecord.</summary>
public sealed record ProductCategorySyncContract
{
    public string Code { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? ParentCategoryId { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
}
