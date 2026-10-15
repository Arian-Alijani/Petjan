namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Read-only image of a server-settled stock entry; never accepted as a client write.</summary>
public sealed record StockLedgerEntrySyncContract
{
    public string ProductId { get; init; } = string.Empty;
    public string VariantId { get; init; } = string.Empty;
    public int Kind { get; init; }
    public long QuantityScaled { get; init; }
    public string QuantityUnit { get; init; } = string.Empty;
    public long UnitCostMinor { get; init; }
    public string UnitCostCurrency { get; init; } = string.Empty;
    public string? LotNumber { get; init; }
    public DateTimeOffset? ExpiryUtc { get; init; }
    public DateTimeOffset OccurredUtc { get; init; }
    public string? SourceEntityType { get; init; }
    public string? SourceEntityId { get; init; }
    public string? OperationId { get; init; }
    public string RecordedByUserId { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public string? ReversesMovementId { get; init; }
}
