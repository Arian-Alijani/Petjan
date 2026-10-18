using Animora.Application.UseCases.Purchasing.ReadModels;

namespace Animora.Application.Ports;

/// <summary>Read projections for the «خرید و تأمین» tab beyond the order snapshot: suppliers and posted receipts.</summary>
public interface IPurchaseDirectoryQueries
{
    /// <summary>Active suppliers, optionally filtered by name / code / phone, ordered by name.</summary>
    Task<IReadOnlyList<SupplierRowReadModel>> ListSuppliersAsync(string? search, int take, CancellationToken ct);

    /// <summary>
    /// The supplier register: every active supplier with its purchase statistics (receipt count, total
    /// purchased, last purchase time and open-order count), optionally filtered by name / code / phone and
    /// ordered by name.
    /// </summary>
    Task<IReadOnlyList<SupplierSummaryReadModel>> ListSupplierSummariesAsync(string? search, int take, CancellationToken ct);

    /// <summary>One supplier's register row (with statistics), or <see langword="null"/> when unknown/inactive.</summary>
    Task<SupplierSummaryReadModel?> FindSupplierSummaryAsync(string supplierId, CancellationToken ct);

    /// <summary>One supplier's completed (posted) purchase history, newest first.</summary>
    Task<IReadOnlyList<SupplierPurchaseRecordReadModel>> ListSupplierPurchasesAsync(
        string supplierId, int take, CancellationToken ct);

    /// <summary>Total active suppliers on record (KPI).</summary>
    Task<int> CountSuppliersAsync(CancellationToken ct);

    /// <summary>The most recent goods receipts (newest first) with their supplier name and cost total.</summary>
    Task<IReadOnlyList<GoodsReceiptRowReadModel>> ListReceiptsAsync(int take, CancellationToken ct);

    /// <summary>Total goods receipts on record (KPI).</summary>
    Task<int> CountReceiptsAsync(CancellationToken ct);
}
