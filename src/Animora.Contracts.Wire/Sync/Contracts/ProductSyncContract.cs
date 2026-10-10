using System.Text.Json;
namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Explicit allowed replication fields; identity and bookkeeping are carried by SyncRecord.</summary>
public sealed record ProductSyncContract
{
    public string Sku { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Brand { get; init; }
    public string CategoryId { get; init; } = string.Empty;
    public int Kind { get; init; }
    public string UnitCode { get; init; } = string.Empty;
    public bool IsPrescriptionOnly { get; init; }
    public bool IsActive { get; init; }
    public bool TrackStock { get; init; }
    public JsonElement? ReorderLevel { get; init; }
    public JsonElement? DefaultCostPrice { get; init; }
    public JsonElement? DefaultSalePrice { get; init; }
    public int? ShelfLifeDays { get; init; }
    public string? Notes { get; init; }
    public JsonElement Variants { get; init; }
}
