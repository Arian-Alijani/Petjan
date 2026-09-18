namespace Animora.Contracts.Purchasing;

/// <summary>
/// Data seam for the Purchase &amp; Suppliers screen. The desktop ViewModel
/// depends only on this interface; the in-memory
/// <c>MockPurchasingQueryService</c> is the single swappable implementation a
/// future backend replaces (R-18).
/// </summary>
public interface IPurchasingQueryService
{
    /// <summary>
    /// The screen's full snapshot, mirroring what <c>Animora-UI/purchase.html</c>
    /// statically renders: the three KPI stat values and the purchase-orders
    /// table rows.
    /// </summary>
    Task<PurchasingSnapshot> GetSnapshotAsync();
}
