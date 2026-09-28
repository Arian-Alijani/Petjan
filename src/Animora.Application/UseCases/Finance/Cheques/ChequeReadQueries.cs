using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Domain.Identity;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Finance.Cheques;

[RequiresPermission(PermissionCodes.ChequesRead)]
public sealed record ChequeListQuery(int Page = 1, int PageSize = 100) : IQuery<PagedResult<ChequeRow>>;
public sealed class ChequeListQueryHandler(IChequeQueries queries) : IQueryHandler<ChequeListQuery, PagedResult<ChequeRow>>
{
    public async Task<Result<PagedResult<ChequeRow>>> HandleAsync(ChequeListQuery q, QueryContext context, CancellationToken ct) =>
        Result<PagedResult<ChequeRow>>.Ok(await queries.GetListAsync(q.Page, q.PageSize, ct));
}

[RequiresPermission(PermissionCodes.ChequesRead)]
public sealed record ChequeCashflowQuery(int Weeks = 8) : IQuery<IReadOnlyList<ChequeCashflowWeek>>;
public sealed class ChequeCashflowQueryHandler(IChequeQueries queries, Animora.Domain.Time.IClock clock)
    : IQueryHandler<ChequeCashflowQuery, IReadOnlyList<ChequeCashflowWeek>>
{
    public async Task<Result<IReadOnlyList<ChequeCashflowWeek>>> HandleAsync(ChequeCashflowQuery q, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<ChequeCashflowWeek>>.Ok(await queries.GetCashflowAsync(q.Weeks,
            DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), ct));
}

[RequiresPermission(PermissionCodes.ChequesRead)]
public sealed record ChequeBankBreakdownQuery : IQuery<IReadOnlyList<ChequeBankRow>>;
public sealed class ChequeBankBreakdownQueryHandler(IChequeQueries queries) : IQueryHandler<ChequeBankBreakdownQuery, IReadOnlyList<ChequeBankRow>>
{
    public async Task<Result<IReadOnlyList<ChequeBankRow>>> HandleAsync(ChequeBankBreakdownQuery q, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<ChequeBankRow>>.Ok(await queries.GetBankBreakdownAsync(ct));
}

[RequiresPermission(PermissionCodes.ChequesRead)]
public sealed record ChequeAlertsQuery : IQuery<IReadOnlyList<ChequeAlertRow>>;
public sealed class ChequeAlertsQueryHandler(IChequeQueries queries, Animora.Domain.Time.IClock clock)
    : IQueryHandler<ChequeAlertsQuery, IReadOnlyList<ChequeAlertRow>>
{
    public async Task<Result<IReadOnlyList<ChequeAlertRow>>> HandleAsync(ChequeAlertsQuery q, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<ChequeAlertRow>>.Ok(await queries.GetAlertsAsync(
            DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), ct));
}
