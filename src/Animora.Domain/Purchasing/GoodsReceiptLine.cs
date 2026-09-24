using Animora.Domain.ValueObjects;

namespace Animora.Domain.Purchasing;

/// <summary>
/// One received line of a <see cref="GoodsReceipt"/>: what actually arrived, in what quantity, at
/// what cost, with its lot and expiry.
/// </summary>
/// <remarks>
/// On <see cref="GoodsReceipt.Post"/> each line becomes exactly one
/// <see cref="Inventory.MovementKind.Receipt"/> movement written by D07/C06 — the receipt itself
/// never touches the ledger. <see cref="Quantity"/> is the <b>positive</b> amount received; the
/// movement factory owns the sign rule.
/// </remarks>
/// <param name="LineId">ULID identifying the line inside its receipt.</param>
/// <param name="ProductId">The received product.</param>
/// <param name="VariantId">The received variant — movements are always per variant.</param>
/// <param name="Quantity">The positive amount received, in the product's unit.</param>
/// <param name="UnitCost">Actual purchase price per unit; feeds the weighted average.</param>
/// <param name="LotNumber">Supplier lot / batch number.</param>
/// <param name="ExpiryUtc">Expiry of the received goods, UTC.</param>
public sealed record GoodsReceiptLine(
    string LineId,
    string ProductId,
    string VariantId,
    Quantity Quantity,
    Money UnitCost,
    string? LotNumber,
    DateTimeOffset? ExpiryUtc)
{
    /// <summary>Materialization-only constructor for the persistence store; all values are then set from columns.</summary>
    private GoodsReceiptLine()
        : this(string.Empty, string.Empty, string.Empty, default, default, null, null)
    {
    }

    /// <summary><c>Quantity × UnitCost</c> through the single rounding boundary.</summary>
    public Money LineCost => UnitCost.ApplyPercent(Quantity.Value * 100m);
}
