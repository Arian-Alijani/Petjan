using Animora.Application.Abstractions;
using Animora.Application.UseCases.Sales.ReadModels;

namespace Animora.Application.Ports;

/// <summary>Tenant-filtered, bounded sales read projections. No write aggregate crosses this seam.</summary>
public interface ISalesQueries
{
    Task<IReadOnlyList<ReadyForBillingReadModel>> GetReadyForBillingAsync(CancellationToken ct);
    Task<PagedResult<InvoiceSummaryReadModel>> GetInvoicesAsync(string? search, int page, int pageSize, CancellationToken ct);
    Task<InvoiceDetailReadModel?> GetInvoiceAsync(string invoiceId, CancellationToken ct);
    Task<SalesCatalogReadModel> GetCatalogAsync(CancellationToken ct);
    Task<IReadOnlyList<InsurerReadModel>> GetInsurersAsync(CancellationToken ct);
    Task<SaleKpiReadModel> GetKpisAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct);
    Task<CashierHistoryPageReadModel> GetHistoryAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc,
        string? search, string? kind, string sort, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<OwnerLookupReadModel>> FindOwnersAsync(string? search, CancellationToken ct);
}
