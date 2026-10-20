using Animora.Domain.Results;
using Animora.Domain.ValueObjects;

namespace Animora.Domain.Inventory;

/// <summary>
/// The <b>single</b> on-hand algorithm of the product (ARCHITECTURE.md §11.2, R-B23): stock is
/// always derived from the <see cref="StockMovement"/> ledger and never read from a stored absolute.
/// </summary>
/// <remarks>
/// <para>
/// Costing is a <b>moving weighted average</b> over inbound movements; outbound movements consume
/// at the running average. FIFO and LIFO are not implemented and are not a future toggle without an
/// ADR.
/// </para>
/// <para>
/// Every method is pure — no state, no clock, no allocation per movement. The lists are walked with
/// indexed <c>for</c> loops and no LINQ (PERFORMANCE.md §2). All movements of one variant share one
/// unit code and one currency; a mixed list is a <b>defect</b> in the caller's query, so it throws
/// rather than returning a <see cref="Result"/> (CONVENTIONS.md §4). The one place a unit mismatch
/// is expected control flow — comparing a request against an on-hand figure — is
/// <see cref="EnsureSufficient"/>, which returns <c>UNIT_MISMATCH</c>.
/// </para>
/// </remarks>
public static class StockCalculator
{
    /// <summary>
    /// On-hand = the signed sum of every movement of the variant. Returns zero in
    /// <c>"EA"</c> for an empty ledger.
    /// </summary>
    /// <exception cref="InvalidOperationException">The movements do not share one unit code.</exception>
    public static Quantity OnHand(IReadOnlyList<StockMovement> movements)
    {
        ArgumentNullException.ThrowIfNull(movements);

        if (movements.Count == 0)
        {
            return Quantity.Zero(DefaultUnitCode);
        }

        var unit = movements[0].Quantity.UnitCode;
        var total = 0m;

        for (var i = 0; i < movements.Count; i++)
        {
            var quantity = movements[i].Quantity;
            EnsureSameUnit(unit, quantity.UnitCode);
            total += quantity.Value;
        }

        return new Quantity(total, unit);
    }

    /// <summary>
    /// The moving weighted-average unit cost after replaying <paramref name="movements"/> in the
    /// order given: inbound movements raise the running value and quantity, outbound movements
    /// consume at the running average. Returns zero for an empty ledger.
    /// </summary>
    /// <remarks>
    /// The running value is accumulated in <see cref="decimal"/> and rounded to the minor unit once,
    /// at the end, with <see cref="MidpointRounding.AwayFromZero"/> — so no intermediate rounding
    /// drift accumulates. This is a derived statistic, not a discount or tax: the single money
    /// rounding boundary of <see cref="Money.ApplyPercent(decimal)"/> is untouched by it.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The movements do not share one unit code or one currency.</exception>
    public static Money WeightedAverageCost(IReadOnlyList<StockMovement> movements)
    {
        ArgumentNullException.ThrowIfNull(movements);

        if (movements.Count == 0)
        {
            return Money.ZeroIrt;
        }

        var unit = movements[0].Quantity.UnitCode;
        var currency = movements[0].UnitCost.Currency;

        var runningQuantity = 0m;
        var runningValue = 0m;
        var average = 0m;

        for (var i = 0; i < movements.Count; i++)
        {
            var movement = movements[i];
            EnsureSameUnit(unit, movement.Quantity.UnitCode);
            EnsureSameCurrency(currency, movement.UnitCost.Currency);

            var quantity = movement.Quantity.Value;
            if (quantity > 0m)
            {
                runningQuantity += quantity;
                runningValue += quantity * movement.UnitCost.Minor;
            }
            else
            {
                // Outbound consumes at the running average, never at its own unit cost.
                runningQuantity += quantity;
                runningValue += quantity * average;
            }

            average = runningQuantity > 0m ? runningValue / runningQuantity : average;

            // Oversell guard: once the ledger is drained to (or below) zero, the running value can
            // go negative while the average freezes at its last positive figure. Dropping the stale
            // negative value here means the next inbound re-seeds a clean average from its own cost
            // only, instead of inheriting a skewed/negative baseline. Average is intentionally kept.
            if (runningQuantity <= 0m)
            {
                runningValue = 0m;
            }
        }

        var rounded = Math.Round(average, 0, MidpointRounding.AwayFromZero);
        return new Money((long)rounded, currency);
    }

    /// <summary>
    /// Guards an outbound movement: fails with <c>INSUFFICIENT_STOCK</c> when
    /// <paramref name="requested"/> exceeds <paramref name="onHand"/> and the caller does not allow
    /// stock to go negative, and with <c>UNIT_MISMATCH</c> when the two figures use different units.
    /// </summary>
    /// <param name="onHand">Current derived on-hand.</param>
    /// <param name="requested">The positive amount about to leave stock.</param>
    /// <param name="allowNegative">
    /// <see langword="true"/> lets the tenant sell into negative stock (a configured clinic policy);
    /// the movement is still recorded, the ledger stays correct.
    /// </param>
    public static Result EnsureSufficient(Quantity onHand, Quantity requested, bool allowNegative)
    {
        if (!string.Equals(onHand.UnitCode, requested.UnitCode, StringComparison.Ordinal))
        {
            return Result.Fail(
                ErrorCodes.UnitMismatch,
                $"On-hand unit '{onHand.UnitCode}' and requested unit '{requested.UnitCode}' differ.");
        }

        if (allowNegative)
        {
            return Result.Ok();
        }

        var needed = Math.Abs(requested.Value);
        return needed > onHand.Value
            ? Result.Fail(
                ErrorCodes.InsufficientStock,
                $"Requested {needed} {requested.UnitCode} but only {onHand.Value} is on hand.")
            : Result.Ok();
    }

    /// <summary>
    /// <see langword="true"/> when <paramref name="onHand"/> has reached or fallen below
    /// <paramref name="reorderLevel"/> — the trigger for <c>StockBelowReorderEvent</c> and the C06
    /// reorder list. A non-positive reorder level never triggers.
    /// </summary>
    /// <exception cref="InvalidOperationException">The two quantities use different units.</exception>
    public static bool IsBelowReorder(Quantity onHand, Quantity reorderLevel)
    {
        EnsureSameUnit(onHand.UnitCode, reorderLevel.UnitCode);
        return reorderLevel.Value > 0m && onHand.Value <= reorderLevel.Value;
    }

    /// <summary>
    /// The earliest expiry carried by the inbound movements, or <see langword="null"/> when none of
    /// them is perishable. Outbound movements are ignored: they carry the expiry of goods leaving.
    /// </summary>
    public static DateTimeOffset? EarliestExpiry(IReadOnlyList<StockMovement> inbound)
    {
        ArgumentNullException.ThrowIfNull(inbound);

        DateTimeOffset? earliest = null;
        for (var i = 0; i < inbound.Count; i++)
        {
            var movement = inbound[i];
            if (!movement.IsInbound)
            {
                continue;
            }

            var expiry = movement.ExpiryUtc;
            if (expiry is not null && (earliest is null || expiry < earliest))
            {
                earliest = expiry;
            }
        }

        return earliest;
    }

    /// <summary>
    /// Projects the full <see cref="StockLevel"/> of one variant from its ledger in a single pass
    /// per statistic. The result is a rebuildable projection — never persisted as an authority.
    /// </summary>
    /// <param name="productId">The product.</param>
    /// <param name="variantId">The variant.</param>
    /// <param name="movements">The variant's complete ledger, oldest first.</param>
    /// <param name="reserved">Amount committed to open orders; pass zero when nothing is reserved.</param>
    public static StockLevel Project(
        string productId,
        string variantId,
        IReadOnlyList<StockMovement> movements,
        Quantity? reserved = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentException.ThrowIfNullOrWhiteSpace(variantId);
        ArgumentNullException.ThrowIfNull(movements);

        var onHand = OnHand(movements);
        var committed = reserved ?? Quantity.Zero(onHand.UnitCode);
        EnsureSameUnit(onHand.UnitCode, committed.UnitCode);

        return new StockLevel(
            productId,
            variantId,
            onHand,
            committed,
            onHand.Subtract(committed),
            WeightedAverageCost(movements),
            EarliestExpiry(movements));
    }

    /// <summary>Unit assumed for an empty ledger: "each".</summary>
    private const string DefaultUnitCode = "EA";

    private static void EnsureSameUnit(string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unit mismatch in the stock ledger: '{expected}' and '{actual}' cannot be combined.");
        }
    }

    private static void EnsureSameCurrency(string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Currency mismatch in the stock ledger: '{expected}' and '{actual}' cannot be combined.");
        }
    }
}
