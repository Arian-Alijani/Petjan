using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>
/// Device-local, maintained on-hand projection for one <c>(TenantId, ProductId, VariantId)</c> key (C06).
/// </summary>
/// <remarks>
/// <para>
/// The append-only <c>StockMovements</c> ledger stays the single authority over stock (R-B23,
/// ARCHITECTURE.md §11.2). This row is a <b>cache with a proof</b>: it is updated inside the same
/// transaction as every added movement (<c>StockSnapshotUpdater</c>) and can always be recomputed from
/// the ledger (<c>ILocalStoreMaintenance.RebuildStockSnapshotsAsync</c>). It never syncs.
/// </para>
/// <para>
/// <see cref="OnHandScaled"/> is the signed sum of <c>StockMovements.QuantityScaled</c> — an exact
/// INTEGER at fixed scale ×1000 (see <c>FixedScaleQuantityConverter</c>) so it compares and sums exactly.
/// </para>
/// </remarks>
[LocalOnly]
public sealed class StockOnHandSnapshot
{
    private StockOnHandSnapshot() { }

    public string Id { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public string ProductId { get; private set; } = string.Empty;
    public string VariantId { get; private set; } = string.Empty;

    /// <summary>Signed on-hand quantity at fixed scale ×1000.</summary>
    public long OnHandScaled { get; private set; }

    public DateTimeOffset LastMovementUtc { get; private set; }
    public string LastMovementId { get; private set; } = string.Empty;

    public static StockOnHandSnapshot Create(string tenantId, string productId, string variantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentException.ThrowIfNullOrWhiteSpace(variantId);
        return new StockOnHandSnapshot
        {
            Id = IdGenerator.NewId(), TenantId = tenantId, ProductId = productId, VariantId = variantId,
        };
    }

    /// <summary>Adds one movement's signed scaled quantity; the latest movement (by time) wins the stamp.</summary>
    public void Apply(long signedScaled, DateTimeOffset occurredUtc, string movementId)
    {
        OnHandScaled = checked(OnHandScaled + signedScaled);
        if (LastMovementId.Length == 0 || occurredUtc >= LastMovementUtc)
        {
            LastMovementUtc = occurredUtc;
            LastMovementId = movementId;
        }
    }

    /// <summary>Overwrites the row with a recomputed total (rebuild / repair path only).</summary>
    public void Reset(long onHandScaled, DateTimeOffset lastMovementUtc, string lastMovementId)
    {
        OnHandScaled = onHandScaled;
        LastMovementUtc = lastMovementUtc;
        LastMovementId = lastMovementId;
    }
}
