namespace Animora.Contracts.Inventory;

/// <summary>A product's aggregate stock status as the list row's badge/bar and the stock chips read it.</summary>
public enum StockStatusKind
{
    /// <summary>No variant is at/below its reorder point.</summary>
    Ok,

    /// <summary>Some (not all) variants are at/below their reorder point.</summary>
    Low,

    /// <summary>Every variant is at/below its reorder point.</summary>
    Out,
}

/// <summary>
/// The single client-side stock-status rule (P2-16). Every Desktop call site —
/// list row badge/bar, inline variant sub-row, product-modal variant cards and
/// the stock-adjust rows — derives «کم‌موجود» from here, so the threshold can
/// never drift between the badge and the per-variant flag. Mirrors the server's
/// integer <c>OnHandScaled &lt;= ReorderLevelScaled</c> predicate that backs
/// <see cref="ProductStockFilter.Low"/>.
/// </summary>
public static class StockStatus
{
    /// <summary>True when <paramref name="stock"/> is at or below <paramref name="reorderPoint"/>.</summary>
    public static bool IsLow(int stock, int reorderPoint) => stock <= reorderPoint;

    /// <summary>True when the variant's on-hand is at or below its reorder point.</summary>
    public static bool IsLow(ProductVariant variant) => IsLow(variant.Stock, variant.ReorderPoint);

    /// <summary>True when the variant is actually depleted (zero on-hand), regardless of its reorder point.</summary>
    public static bool IsDepleted(ProductVariant variant) => variant.Stock <= 0;

    /// <summary>
    /// The product's aggregate status. «اتمام»/<see cref="StockStatusKind.Out"/> is reserved for a real
    /// depletion — every variant has zero on-hand — so a product that still holds stock is never flagged as
    /// finished. When stock remains but any variant is at/below its reorder point the product reads as
    /// <see cref="StockStatusKind.Low"/> («هشدار»); otherwise <see cref="StockStatusKind.Ok"/>.
    /// A product without variants reads as <see cref="StockStatusKind.Out"/>.
    /// </summary>
    public static StockStatusKind Of(IReadOnlyList<ProductVariant> variants)
    {
        if (variants.Count == 0) return StockStatusKind.Out;

        var depleted = 0;
        var low = 0;
        for (var i = 0; i < variants.Count; i++)
        {
            if (IsDepleted(variants[i])) depleted++;
            if (IsLow(variants[i])) low++;
        }

        // Only a product whose every variant is truly out of stock is «اتمام».
        if (depleted == variants.Count) return StockStatusKind.Out;

        // Stock still exists somewhere: a reorder-point breach (or a partially depleted variant) is a warning,
        // not an out-of-stock state.
        return low > 0 ? StockStatusKind.Low : StockStatusKind.Ok;
    }

    /// <summary>The presentation key (<c>ok</c> / <c>low</c> / <c>out</c>) the inventory palette switches on.</summary>
    public static string Key(StockStatusKind kind) => kind switch
    {
        StockStatusKind.Ok => "ok",
        StockStatusKind.Low => "low",
        _ => "out",
    };
}
