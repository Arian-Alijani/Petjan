using Animora.Domain.ValueObjects;

namespace Animora.Domain.Purchasing;

/// <summary>
/// One ordered line of a <see cref="PurchaseOrder"/>: what variant, how much of it, at what cost,
/// and how much of it has arrived so far.
/// </summary>
/// <remarks>
/// All money on the line goes through <see cref="Money.ApplyPercent(decimal)"/> — the single
/// rounding boundary of the product (CONVENTIONS.md §4). That includes the quantity extension:
/// a quantity is a fraction of a unit cost, so <c>q × unitCost</c> is expressed as
/// <c>unitCost.ApplyPercent(q × 100)</c> rather than an inline <c>Math.Round</c>.
/// </remarks>
/// <param name="LineId">ULID identifying the line inside its order.</param>
/// <param name="ProductId">The ordered product.</param>
/// <param name="VariantId">The ordered variant — movements are always per variant.</param>
/// <param name="OrderedQuantity">How much was ordered, positive, in the product's unit.</param>
/// <param name="ReceivedQuantity">How much has been received so far, in the same unit.</param>
/// <param name="UnitCost">Agreed purchase price per unit.</param>
/// <param name="DiscountPercent">Line discount in percent, applied to the gross.</param>
/// <param name="TaxPercent">Line tax in percent, applied after the discount.</param>
/// <param name="LotNumber">Expected supplier lot / batch number.</param>
/// <param name="ExpiryUtc">Expected expiry of the goods, UTC.</param>
public sealed record PurchaseOrderLine(
    string LineId,
    string ProductId,
    string VariantId,
    Quantity OrderedQuantity,
    Quantity ReceivedQuantity,
    Money UnitCost,
    decimal DiscountPercent,
    decimal TaxPercent,
    string? LotNumber,
    DateTimeOffset? ExpiryUtc)
{
    /// <summary><c>OrderedQuantity × UnitCost</c>, before discount and tax.</summary>
    public Money Gross => UnitCost.ApplyPercent(OrderedQuantity.Value * 100m);

    /// <summary>The discount amount taken off <see cref="Gross"/>.</summary>
    public Money DiscountAmount => Gross.ApplyPercent(DiscountPercent);

    /// <summary><see cref="Gross"/> minus <see cref="DiscountAmount"/>: the taxable base.</summary>
    public Money NetAmount => Gross.Subtract(DiscountAmount);

    /// <summary>The tax charged on <see cref="NetAmount"/>.</summary>
    public Money TaxAmount => NetAmount.ApplyPercent(TaxPercent);

    /// <summary><see cref="NetAmount"/> plus <see cref="TaxAmount"/>: what the line costs.</summary>
    public Money LineTotal => NetAmount.Add(TaxAmount);

    /// <summary>How much of the line is still outstanding.</summary>
    public Quantity OutstandingQuantity => OrderedQuantity.Subtract(ReceivedQuantity);

    /// <summary><see langword="true"/> once at least the ordered quantity has arrived.</summary>
    public bool IsFullyReceived => ReceivedQuantity.Value >= OrderedQuantity.Value;
}
