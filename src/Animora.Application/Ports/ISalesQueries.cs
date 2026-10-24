using Animora.Application.Abstractions;
using Animora.Application.UseCases.Sales.ReadModels;

namespace Animora.Application.Ports;

/// <summary>Tenant-filtered, bounded sales read projections. No write aggregate crosses this seam.</summary>
public interface ISalesQueries
{
    Task<IReadOnlyList<ReadyForBillingReadModel>> GetReadyForBillingAsync(CancellationToken ct);
    Task<PagedResult<InvoiceSummaryReadModel>> GetInvoicesAsync(string? search, int page, int pageSize, CancellationToken ct);
    /// <summary>The newest <paramref name="take"/> invoices of one owner (bounded, newest first).</summary>
    Task<IReadOnlyList<InvoiceSummaryReadModel>> GetOwnerInvoicesAsync(string ownerId, int take, CancellationToken ct);
    Task<InvoiceDetailReadModel?> GetInvoiceAsync(string invoiceId, CancellationToken ct);
    Task<SalesCatalogReadModel> GetCatalogAsync(CancellationToken ct);
    /// <summary>
    /// The sellable catalog row of the active variant carrying <paramref name="barcode"/> (exact match, active product),
    /// shaped like a <see cref="GetCatalogAsync"/> row (id = the variant id); null when no variant has that barcode.
    /// </summary>
    Task<SalesCatalogItemReadModel?> FindCatalogItemByBarcodeAsync(string barcode, CancellationToken ct);
    Task<IReadOnlyList<InsurerReadModel>> GetInsurersAsync(CancellationToken ct);
    Task<SaleKpiReadModel> GetKpisAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct);
    Task<CashierHistoryPageReadModel> GetHistoryAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc,
        string? search, string? kind, string sort, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<OwnerLookupReadModel>> FindOwnersAsync(string? search, CancellationToken ct);
}
