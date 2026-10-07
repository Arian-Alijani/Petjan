using Animora.Application.UseCases.Purchasing.ReadModels;

namespace Animora.Application.Ports;

/// <summary>Read projections for the «خرید و تأمین» tab beyond the order snapshot: suppliers and posted receipts.</summary>
public interface IPurchaseDirectoryQueries
{
    /// <summary>Active suppliers, optionally filtered by name / code / phone, ordered by name.</summary>
    Task<IReadOnlyList<SupplierRowReadModel>> ListSuppliersAsync(string? search, int take, CancellationToken ct);

    /// <summary>Total active suppliers on record (KPI).</summary>
    Task<int> CountSuppliersAsync(CancellationToken ct);

    /// <summary>The most recent goods receipts (newest first) with their supplier name and cost total.</summary>
    Task<IReadOnlyList<GoodsReceiptRowReadModel>> ListReceiptsAsync(int take, CancellationToken ct);

    /// <summary>Total goods receipts on record (KPI).</summary>
    Task<int> CountReceiptsAsync(CancellationToken ct);
}
