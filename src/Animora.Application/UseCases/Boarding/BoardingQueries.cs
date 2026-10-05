using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Boarding.ReadModels;
using Animora.Application.UseCases.Sales.ReadModels;
using Animora.Domain.Boarding;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Boarding;

[RequiresPermission(PermissionCodes.BoardingRead)]
public sealed record BoardingStayListQuery : IQuery<IReadOnlyList<StayListItemReadModel>>;
public sealed class BoardingStayListQueryHandler(IBoardingQueries queries)
    : IQueryHandler<BoardingStayListQuery, IReadOnlyList<StayListItemReadModel>>
{
    public async Task<Result<IReadOnlyList<StayListItemReadModel>>> HandleAsync(BoardingStayListQuery query, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<StayListItemReadModel>>.Ok(await queries.GetStaysAsync(ct));
}

[RequiresPermission(PermissionCodes.BoardingRead)]
public sealed record BoardingStayDetailQuery(string StayId) : IQuery<StayDetailReadModel?>;
public sealed class BoardingStayDetailQueryHandler(IBoardingQueries queries)
    : IQueryHandler<BoardingStayDetailQuery, StayDetailReadModel?>
{
    public async Task<Result<StayDetailReadModel?>> HandleAsync(BoardingStayDetailQuery query, QueryContext context, CancellationToken ct) =>
        Result<StayDetailReadModel?>.Ok(await queries.GetDetailAsync(query.StayId, ct));
}

[RequiresPermission(PermissionCodes.BoardingRead)]
public sealed record BoardingRoomsQuery : IQuery<IReadOnlyList<RoomOccupancyReadModel>>;
public sealed class BoardingRoomsQueryHandler(IBoardingQueries queries)
    : IQueryHandler<BoardingRoomsQuery, IReadOnlyList<RoomOccupancyReadModel>>
{
    public async Task<Result<IReadOnlyList<RoomOccupancyReadModel>>> HandleAsync(BoardingRoomsQuery query, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<RoomOccupancyReadModel>>.Ok(await queries.GetRoomsAsync(ct));
}

[RequiresPermission(PermissionCodes.BoardingRead)]
public sealed record BoardingServicesQuery : IQuery<IReadOnlyList<BoardingServiceDef>>;
public sealed class BoardingServicesQueryHandler(IBoardingQueries queries)
    : IQueryHandler<BoardingServicesQuery, IReadOnlyList<BoardingServiceDef>>
{
    public async Task<Result<IReadOnlyList<BoardingServiceDef>>> HandleAsync(BoardingServicesQuery query, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<BoardingServiceDef>>.Ok(await queries.GetServicesAsync(ct));
}

/// <summary>
/// The stay's «افزودن خدمت یا کالا» catalog. Single source of truth: the very same projection the Sales picker
/// uses (variant-priced goods + clinic services + boarding services), gated by boarding access so reception
/// staff without sales rights can still add lines to a stay.
/// </summary>
[RequiresPermission(PermissionCodes.BoardingRead)]
public sealed record BoardingCatalogQuery : IQuery<SalesCatalogReadModel>;
public sealed class BoardingCatalogQueryHandler(ISalesQueries sales)
    : IQueryHandler<BoardingCatalogQuery, SalesCatalogReadModel>
{
    public async Task<Result<SalesCatalogReadModel>> HandleAsync(BoardingCatalogQuery query, QueryContext context, CancellationToken ct) =>
        Result<SalesCatalogReadModel>.Ok(await sales.GetCatalogAsync(ct));
}

[RequiresPermission(PermissionCodes.BoardingRead)]
public sealed record BoardingKnownPetsQuery(string? Search = null) : IQuery<IReadOnlyList<BoardingKnownPetReadModel>>;
public sealed class BoardingKnownPetsQueryHandler(IBoardingQueries queries)
    : IQueryHandler<BoardingKnownPetsQuery, IReadOnlyList<BoardingKnownPetReadModel>>
{
    public async Task<Result<IReadOnlyList<BoardingKnownPetReadModel>>> HandleAsync(BoardingKnownPetsQuery query, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<BoardingKnownPetReadModel>>.Ok(await queries.GetKnownPetsAsync(query.Search, ct));
}

[RequiresPermission(PermissionCodes.BoardingRead)]
public sealed record BoardingHistoryQuery(BoardingHistoryCriteria Criteria) : IQuery<BoardingHistoryResult>;
public sealed class BoardingHistoryQueryHandler(IBoardingQueries queries)
    : IQueryHandler<BoardingHistoryQuery, BoardingHistoryResult>
{
    public async Task<Result<BoardingHistoryResult>> HandleAsync(BoardingHistoryQuery query, QueryContext context, CancellationToken ct) =>
        Result<BoardingHistoryResult>.Ok(await queries.GetHistoryAsync(query.Criteria, ct));
}
