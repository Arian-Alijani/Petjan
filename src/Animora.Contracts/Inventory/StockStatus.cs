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

    /// <summary>
    /// The product's aggregate status: all variants low → <see cref="StockStatusKind.Out"/>;
    /// some → <see cref="StockStatusKind.Low"/>; none → <see cref="StockStatusKind.Ok"/>.
    /// A product without variants reads as <see cref="StockStatusKind.Out"/>.
    /// </summary>
    public static StockStatusKind Of(IReadOnlyList<ProductVariant> variants)
    {
        if (variants.Count == 0) return StockStatusKind.Out;
        var low = 0;
        for (var i = 0; i < variants.Count; i++)
        {
            if (IsLow(variants[i])) low++;
        }
        return low == variants.Count ? StockStatusKind.Out : low > 0 ? StockStatusKind.Low : StockStatusKind.Ok;
    }

    /// <summary>The presentation key (<c>ok</c> / <c>low</c> / <c>out</c>) the inventory palette switches on.</summary>
    public static string Key(StockStatusKind kind) => kind switch
    {
        StockStatusKind.Ok => "ok",
        StockStatusKind.Low => "low",
        _ => "out",
    };
}
