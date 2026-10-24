using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Sales.ReadModels;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Sales;

/// <summary>
/// The sales read side (C07): handoff queue, invoice list/detail, catalog, insurers, KPIs, cashier
/// history and the owner lookup. Every handler is a thin projection over <see cref="ISalesQueries"/> —
/// no aggregate is ever loaded to answer a read (A-RULE 018), and the paging/sort/sums are applied in
/// the store (PERFORMANCE §6) rather than in memory here.
/// </summary>
[RequiresPermission(PermissionCodes.SalesRead)]
public sealed record ReadyForBillingQuery : IQuery<IReadOnlyList<ReadyForBillingReadModel>>;

public sealed class ReadyForBillingQueryHandler(ISalesQueries queries)
    : IQueryHandler<ReadyForBillingQuery, IReadOnlyList<ReadyForBillingReadModel>>
{
    public async Task<Result<IReadOnlyList<ReadyForBillingReadModel>>> HandleAsync(
        ReadyForBillingQuery query, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<ReadyForBillingReadModel>>.Ok(await queries.GetReadyForBillingAsync(ct));
}

/// <summary>One page of the invoice list, newest first; the search term matches number, owner or guest.</summary>
[RequiresPermission(PermissionCodes.SalesRead)]
public sealed record InvoiceListQuery(string? Search = null, int Page = 1, int PageSize = 50)
    : IQuery<PagedResult<InvoiceSummaryReadModel>>;

public sealed class InvoiceListQueryHandler(ISalesQueries queries)
    : IQueryHandler<InvoiceListQuery, PagedResult<InvoiceSummaryReadModel>>
{
    public async Task<Result<PagedResult<InvoiceSummaryReadModel>>> HandleAsync(
        InvoiceListQuery query, QueryContext context, CancellationToken ct) =>
        Result<PagedResult<InvoiceSummaryReadModel>>.Ok(await queries.GetInvoicesAsync(
            query.Search, query.Page, query.PageSize, ct));
}

/// <summary>The newest invoices registered for one owner (owner file); bounded by <paramref name="Take"/>.</summary>
[RequiresPermission(PermissionCodes.SalesRead)]
public sealed record OwnerInvoicesQuery(string OwnerId, int Take = 50)
    : IQuery<IReadOnlyList<InvoiceSummaryReadModel>>;

public sealed class OwnerInvoicesQueryHandler(ISalesQueries queries)
    : IQueryHandler<OwnerInvoicesQuery, IReadOnlyList<InvoiceSummaryReadModel>>
{
    private const int HardMaxTake = 200;

    public async Task<Result<IReadOnlyList<InvoiceSummaryReadModel>>> HandleAsync(
        OwnerInvoicesQuery query, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<InvoiceSummaryReadModel>>.Ok(await queries.GetOwnerInvoicesAsync(
            query.OwnerId, Math.Clamp(query.Take, 1, HardMaxTake), ct));
}

/// <summary>One invoice with its lines, payments, adjustments and its settlement verdict (R-B16).</summary>
[RequiresPermission(PermissionCodes.SalesRead)]
public sealed record InvoiceDetailQuery(string InvoiceId) : IQuery<InvoiceDetailReadModel?>;

public sealed class InvoiceDetailQueryHandler(ISalesQueries queries)
    : IQueryHandler<InvoiceDetailQuery, InvoiceDetailReadModel?>
{
    public async Task<Result<InvoiceDetailReadModel?>> HandleAsync(
        InvoiceDetailQuery query, QueryContext context, CancellationToken ct) =>
        Result<InvoiceDetailReadModel?>.Ok(await queries.GetInvoiceAsync(query.InvoiceId, ct));
}

/// <summary>The picker catalog: every sellable service/item plus the filter chips and the recent strip.</summary>
[RequiresPermission(PermissionCodes.SalesRead)]
public sealed record SalesCatalogQuery : IQuery<SalesCatalogReadModel>;

public sealed class SalesCatalogQueryHandler(ISalesQueries queries)
    : IQueryHandler<SalesCatalogQuery, SalesCatalogReadModel>
{
    public async Task<Result<SalesCatalogReadModel>> HandleAsync(
        SalesCatalogQuery query, QueryContext context, CancellationToken ct) =>
        Result<SalesCatalogReadModel>.Ok(await queries.GetCatalogAsync(ct));
}

/// <summary>Resolves a scanned barcode to the sellable catalog item (variant) that carries it, or null.</summary>
[RequiresPermission(PermissionCodes.SalesRead)]
public sealed record SalesCatalogByBarcodeQuery(string Barcode) : IQuery<SalesCatalogItemReadModel?>;

public sealed class SalesCatalogByBarcodeQueryHandler(ISalesQueries queries)
    : IQueryHandler<SalesCatalogByBarcodeQuery, SalesCatalogItemReadModel?>
{
    public async Task<Result<SalesCatalogItemReadModel?>> HandleAsync(
        SalesCatalogByBarcodeQuery query, QueryContext context, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(query.Barcode)
            ? Result<SalesCatalogItemReadModel?>.Ok(null)
            : Result<SalesCatalogItemReadModel?>.Ok(await queries.FindCatalogItemByBarcodeAsync(query.Barcode.Trim(), ct));
}

/// <summary>The reference insurers offered in the invoice's «پوشش بیمه» select.</summary>
[RequiresPermission(PermissionCodes.SalesRead)]
public sealed record InsurersQuery : IQuery<IReadOnlyList<InsurerReadModel>>;

public sealed class InsurersQueryHandler(ISalesQueries queries)
    : IQueryHandler<InsurersQuery, IReadOnlyList<InsurerReadModel>>
{
    public async Task<Result<IReadOnlyList<InsurerReadModel>>> HandleAsync(
        InsurersQuery query, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<InsurerReadModel>>.Ok(await queries.GetInsurersAsync(ct));
}

/// <summary>
/// The three inline mini-KPIs over the supplied window. The window is resolved by the caller
/// (the client service, via <see cref="IClock"/> + the clinic time zone) so this handler stays a pure read.
/// </summary>
[RequiresPermission(PermissionCodes.SalesRead)]
public sealed record SaleKpiQuery(DateTimeOffset FromUtc, DateTimeOffset ToUtc) : IQuery<SaleKpiReadModel>;

public sealed class SaleKpiQueryHandler(ISalesQueries queries)
    : IQueryHandler<SaleKpiQuery, SaleKpiReadModel>
{
    public async Task<Result<SaleKpiReadModel>> HandleAsync(
        SaleKpiQuery query, QueryContext context, CancellationToken ct) =>
        Result<SaleKpiReadModel>.Ok(await queries.GetKpisAsync(query.FromUtc, query.ToUtc, ct));
}

/// <summary>
/// One page of the cashier ledger. The store applies the filter, sort and paging, and computes the
/// inflow/outflow sums over the <b>whole</b> filtered set so the totals never shift with the page.
/// </summary>
[RequiresPermission(PermissionCodes.SalesRead)]
public sealed record SalesHistoryQuery(
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? Search = null,
    string? Kind = null,
    string Sort = "date",
    int Page = 1,
    int PageSize = 50) : IQuery<CashierHistoryPageReadModel>;

public sealed class SalesHistoryQueryHandler(ISalesQueries queries)
    : IQueryHandler<SalesHistoryQuery, CashierHistoryPageReadModel>
{
    public async Task<Result<CashierHistoryPageReadModel>> HandleAsync(
        SalesHistoryQuery query, QueryContext context, CancellationToken ct) =>
        Result<CashierHistoryPageReadModel>.Ok(await queries.GetHistoryAsync(
            query.FromUtc, query.ToUtc, query.Search, query.Kind, query.Sort, query.Page, query.PageSize, ct));
}

/// <summary>The owner directory backing «انتخاب مشتری», optionally filtered by name/phone/file code.</summary>
[RequiresPermission(PermissionCodes.SalesRead)]
public sealed record OwnerLookupQuery(string? Search = null) : IQuery<IReadOnlyList<OwnerLookupReadModel>>;

public sealed class OwnerLookupQueryHandler(ISalesQueries queries)
    : IQueryHandler<OwnerLookupQuery, IReadOnlyList<OwnerLookupReadModel>>
{
    public async Task<Result<IReadOnlyList<OwnerLookupReadModel>>> HandleAsync(
        OwnerLookupQuery query, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<OwnerLookupReadModel>>.Ok(await queries.FindOwnersAsync(query.Search, ct));
}
