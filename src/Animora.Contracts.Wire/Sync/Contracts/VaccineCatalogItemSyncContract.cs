using System.Text.Json;
namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Explicit allowed replication fields; identity and bookkeeping are carried by SyncRecord.</summary>
public sealed record VaccineCatalogItemSyncContract
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Protects { get; init; }
    public int TotalDoses { get; init; }
    public int BoosterDays { get; init; }
    public int Stock { get; init; }
    public int MinStock { get; init; }
    public bool IsActive { get; init; }
}
