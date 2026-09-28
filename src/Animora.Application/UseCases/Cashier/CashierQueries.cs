using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Sales.ReadModels;
using Animora.Domain.Identity;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Cashier;

[RequiresPermission(PermissionCodes.SalesRead)]
public sealed record CashierInvoiceListQuery(CashierInvoiceTab Tab = CashierInvoiceTab.All, int Page = 1, int PageSize = 50)
    : IQuery<PagedResult<InvoiceSummaryReadModel>>;

public sealed class CashierInvoiceListQueryHandler(ICashierQueries queries)
    : IQueryHandler<CashierInvoiceListQuery, PagedResult<InvoiceSummaryReadModel>>
{
    public async Task<Result<PagedResult<InvoiceSummaryReadModel>>> HandleAsync(CashierInvoiceListQuery q, QueryContext context, CancellationToken ct) =>
        Result<PagedResult<InvoiceSummaryReadModel>>.Ok(await queries.GetInvoicesAsync(q.Tab, q.Page, q.PageSize, ct));
}

[RequiresPermission(PermissionCodes.SalesRead)]
public sealed record CashierStatsQuery(DateTimeOffset FromUtc, DateTimeOffset ToUtc) : IQuery<CashierStatsReadModel>;
public sealed class CashierStatsQueryHandler(ICashierQueries queries) : IQueryHandler<CashierStatsQuery, CashierStatsReadModel>
{
    public async Task<Result<CashierStatsReadModel>> HandleAsync(CashierStatsQuery q, QueryContext context, CancellationToken ct) =>
        Result<CashierStatsReadModel>.Ok(await queries.GetStatsAsync(q.FromUtc, q.ToUtc, ct));
}

[RequiresPermission(PermissionCodes.SalesRead)]
public sealed record CashierTabCountsQuery : IQuery<CashierTabCountsReadModel>;
public sealed class CashierTabCountsQueryHandler(ICashierQueries queries) : IQueryHandler<CashierTabCountsQuery, CashierTabCountsReadModel>
{
    public async Task<Result<CashierTabCountsReadModel>> HandleAsync(CashierTabCountsQuery q, QueryContext context, CancellationToken ct) =>
        Result<CashierTabCountsReadModel>.Ok(await queries.GetTabCountsAsync(ct));
}

[RequiresPermission(PermissionCodes.SalesRead)]
public sealed record OpenCashSessionQuery : IQuery<OpenCashSessionReadModel?>;
public sealed class OpenCashSessionQueryHandler(ICashierQueries queries) : IQueryHandler<OpenCashSessionQuery, OpenCashSessionReadModel?>
{
    public async Task<Result<OpenCashSessionReadModel?>> HandleAsync(OpenCashSessionQuery q, QueryContext context, CancellationToken ct) =>
        Result<OpenCashSessionReadModel?>.Ok(await queries.GetOpenSessionAsync(context.UserId, ct));
}
