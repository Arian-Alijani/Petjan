using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Domain.Finance;
using Animora.Domain.Identity;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Finance.Cheques;

/// <summary>The paged, filtered cheque list (direction / lifecycle scope / search), resolved server-side.</summary>
[RequiresPermission(PermissionCodes.ChequesRead)]
public sealed record ChequeListQuery(ChequeDirection? Direction = null, ChequeListScope Scope = ChequeListScope.All,
    string? Search = null, int Page = 1, int PageSize = 50) : IQuery<PagedResult<ChequeRow>>;

public sealed class ChequeListQueryHandler(IChequeQueries queries)
    : IQueryHandler<ChequeListQuery, PagedResult<ChequeRow>>
{
    public async Task<Result<PagedResult<ChequeRow>>> HandleAsync(ChequeListQuery q, QueryContext context, CancellationToken ct) =>
        Result<PagedResult<ChequeRow>>.Ok(await queries.GetListAsync(
            new ChequeListCriteria(q.Direction, q.Scope, q.Search, q.Page, q.PageSize), ct));
}

/// <summary>One cheque with its append-only history timeline (the detail sheet).</summary>
[RequiresPermission(PermissionCodes.ChequesRead)]
public sealed record ChequeDetailQuery(string ChequeId) : IQuery<ChequeDetailRow>;

public sealed class ChequeDetailQueryHandler(IChequeQueries queries) : IQueryHandler<ChequeDetailQuery, ChequeDetailRow>
{
    public async Task<Result<ChequeDetailRow>> HandleAsync(ChequeDetailQuery q, QueryContext context, CancellationToken ct) =>
        await queries.GetDetailAsync(q.ChequeId, ct) is { } detail
            ? Result<ChequeDetailRow>.Ok(detail)
            : Result<ChequeDetailRow>.Fail(ErrorCodes.ChequeNotFound);
}

/// <summary>Open-portfolio totals (received / issued / bounced) for the screen header.</summary>
[RequiresPermission(PermissionCodes.ChequesRead)]
public sealed record ChequeSummaryQuery : IQuery<ChequeSummaryRow>;

public sealed class ChequeSummaryQueryHandler(IChequeQueries queries) : IQueryHandler<ChequeSummaryQuery, ChequeSummaryRow>
{
    public async Task<Result<ChequeSummaryRow>> HandleAsync(ChequeSummaryQuery q, QueryContext context, CancellationToken ct) =>
        Result<ChequeSummaryRow>.Ok(await queries.GetSummaryAsync(ct));
}

/// <summary>Owners the register form can pick as the drawer of an inbound cheque.</summary>
[RequiresPermission(PermissionCodes.ChequesManage)]
public sealed record ChequeOwnerLookupQuery(string? Search = null) : IQuery<IReadOnlyList<ChequePartyRow>>;

public sealed class ChequeOwnerLookupQueryHandler(IChequeQueries queries)
    : IQueryHandler<ChequeOwnerLookupQuery, IReadOnlyList<ChequePartyRow>>
{
    public async Task<Result<IReadOnlyList<ChequePartyRow>>> HandleAsync(ChequeOwnerLookupQuery q, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<ChequePartyRow>>.Ok(await queries.FindOwnersAsync(q.Search, ct));
}

/// <summary>Suppliers the register form can pick as the payee of an outbound cheque.</summary>
[RequiresPermission(PermissionCodes.ChequesManage)]
public sealed record ChequeSupplierLookupQuery(string? Search = null) : IQuery<IReadOnlyList<ChequePartyRow>>;

public sealed class ChequeSupplierLookupQueryHandler(IChequeQueries queries)
    : IQueryHandler<ChequeSupplierLookupQuery, IReadOnlyList<ChequePartyRow>>
{
    public async Task<Result<IReadOnlyList<ChequePartyRow>>> HandleAsync(ChequeSupplierLookupQuery q, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<ChequePartyRow>>.Ok(await queries.FindSuppliersAsync(q.Search, ct));
}

public static class ChequeRegistration
{
    public static Animora.Application.Dispatch.ApplicationRegistrationBuilder AddChequeUseCases(
        this Animora.Application.Dispatch.ApplicationRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddQuery<ChequeListQuery, PagedResult<ChequeRow>, ChequeListQueryHandler>()
            .AddQuery<ChequeDetailQuery, ChequeDetailRow, ChequeDetailQueryHandler>()
            .AddQuery<ChequeSummaryQuery, ChequeSummaryRow, ChequeSummaryQueryHandler>()
            .AddQuery<ChequeOwnerLookupQuery, IReadOnlyList<ChequePartyRow>, ChequeOwnerLookupQueryHandler>()
            .AddQuery<ChequeSupplierLookupQuery, IReadOnlyList<ChequePartyRow>, ChequeSupplierLookupQueryHandler>()
            .AddCommand<RegisterChequeCommand, string, RegisterChequeCommandHandler>()
            .AddValidator<RegisterChequeCommand, RegisterChequeCommandValidator>()
            .AddCommand<UpdateChequeMetadataCommand, string, UpdateChequeMetadataCommandHandler>()
            .AddValidator<UpdateChequeMetadataCommand, UpdateChequeMetadataCommandValidator>()
            .AddCommand<DeleteChequeDraftCommand, string, DeleteChequeDraftCommandHandler>()
            .AddValidator<DeleteChequeDraftCommand, DeleteChequeDraftCommandValidator>()
            .AddCommand<TransitionChequeCommand, string, TransitionChequeCommandHandler>()
            .AddValidator<TransitionChequeCommand, TransitionChequeCommandValidator>();
    }
}
