using Animora.Domain.ValueObjects;

namespace Animora.Domain.Inventory;

/// <summary>
/// The derived stock position of one product variant at one instant.
/// </summary>
/// <remarks>
/// <b>A projection, never an authority.</b> Every field here is computed from the variant's
/// <see cref="StockMovement"/> ledger by <see cref="StockCalculator"/> (ARCHITECTURE.md §11.2,
/// R-B23). A10 may persist it as a <c>[LocalOnly]</c> cached read model for fast list screens, but
/// that cache is always rebuildable from the movements and is never replicated, never reconciled
/// and never the source of truth. If the cache and the ledger disagree, the ledger wins.
/// </remarks>
/// <param name="ProductId">The product.</param>
/// <param name="VariantId">The variant this position belongs to.</param>
/// <param name="OnHand">Signed sum of the variant's movements.</param>
/// <param name="Reserved">Committed to open orders but not yet moved out (A07).</param>
/// <param name="Available"><c>OnHand - Reserved</c>: what may still be committed.</param>
/// <param name="AverageCost">Moving weighted-average unit cost over the inbound movements.</param>
/// <param name="EarliestExpiryUtc">Earliest expiry among inbound movements; <see langword="null"/> when none.</param>
public sealed record StockLevel(
    string ProductId,
    string VariantId,
    Quantity OnHand,
    Quantity Reserved,
    Quantity Available,
    Money AverageCost,
    DateTimeOffset? EarliestExpiryUtc)
{
    /// <summary>An empty position in <paramref name="unitCode"/> and <paramref name="currency"/>.</summary>
    public static StockLevel Empty(string productId, string variantId, string unitCode, string currency) =>
        new(
            productId,
            variantId,
            Quantity.Zero(unitCode),
            Quantity.Zero(unitCode),
            Quantity.Zero(unitCode),
            Money.Zero(currency),
            null);

    /// <summary><see langword="true"/> when nothing is on hand.</summary>
    public bool IsOutOfStock => OnHand.Value <= 0m;
}
