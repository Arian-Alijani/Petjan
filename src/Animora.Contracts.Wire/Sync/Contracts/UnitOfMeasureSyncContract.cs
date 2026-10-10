using System.Text.Json;
namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Explicit allowed replication fields; identity and bookkeeping are carried by SyncRecord.</summary>
public sealed record UnitOfMeasureSyncContract
{
    public string Code { get; init; } = string.Empty;
    public string DisplayKey { get; init; } = string.Empty;
    public int DecimalPlaces { get; init; }
    public string? BaseUnitCode { get; init; }
    public decimal? ConversionToBase { get; init; }
}
