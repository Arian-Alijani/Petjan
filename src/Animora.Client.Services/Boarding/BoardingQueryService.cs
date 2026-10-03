using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Boarding;
using Animora.Application.UseCases.Boarding.ReadModels;
using Animora.Contracts.Boarding;
using StayPaymentMode = Animora.Domain.Boarding.StayPaymentMode;
using Animora.Domain.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Boarding;

/// <summary>Real boarding seam: every operation owns a scope over the dispatcher.</summary>
public sealed class BoardingQueryService(IServiceScopeFactory scopes) : IBoardingQueryService
{
    private Task<T> QueryAsync<T>(IQuery<T> query) => QueryAsync(query, null);

    /// <summary>
    /// Runs a query, reusing <paramref name="shared"/> when supplied so one operation
    /// (stays + services + clock + zone) resolves its dependencies through a single
    /// DI scope instead of one scope per read (X34).
    /// </summary>
    private async Task<T> QueryAsync<T>(IQuery<T> query, IServiceProvider? shared)
    {
        AsyncServiceScope? own = shared is null ? scopes.CreateAsyncScope() : null;
        try
        {
            var services = shared ?? own!.Value.ServiceProvider;
            var provider = services.GetRequiredService<ICurrentUserProvider>();
            var context = provider.BuildQueryContext();
            if (context.IsFailure) throw new ServiceException(context.Error);
            var result = await services.GetRequiredService<IDispatcher>().QueryAsync(query, context.Value!);
            if (result.IsFailure) throw new ServiceException(result.Error);
            return result.Value!;
        }
        finally
        {
            if (own is { } scope) await scope.DisposeAsync();
        }
    }

    private async Task<T> SendAsync<T>(ICommand<T> command)
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var context = await provider.BuildCommandContextAsync(null, null, null, default);
        if (context.IsFailure) throw new ServiceException(context.Error);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command, context.Value!);
        if (result.IsFailure) throw new ServiceException(result.Error);
        return result.Value!;
    }

    private static (DateTimeOffset Now, TimeZoneInfo Zone) Calendar(IServiceProvider services) =>
        (services.GetRequiredService<IClock>().UtcNow,
            services.GetRequiredService<IClinicTimeZoneProvider>().GetClinicTimeZone());

    public async Task<IReadOnlyList<BoardingStay>> GetStaysAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var rows = await QueryAsync(new BoardingStayListQuery(), sp);
        var services = await QueryAsync(new BoardingServicesQuery(), sp);
        var (now, zone) = Calendar(sp);
        return rows.Select(r => r.ToContract(services, now, zone)).ToArray();
    }

    public async Task<StayDetail?> GetDetailAsync(string stayId)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var row = await QueryAsync(new BoardingStayDetailQuery(stayId), sp);
        if (row is null) return null;
        var services = await QueryAsync(new BoardingServicesQuery(), sp);
        var (now, zone) = Calendar(sp);
        return row.ToContract(services, now, zone);
    }

    public async Task AddItemAsync(string stayId, string itemRef, int quantity) =>
        _ = await SendAsync(new AddStayItemCommand(stayId, itemRef, quantity));

    public async Task RemoveItemAsync(string stayId, int index)
    {
        var detail = await QueryAsync(new BoardingStayDetailQuery(stayId));
        if (detail is null) throw new InvalidOperationException("STAY_NOT_FOUND");
        var item = detail.Items.ElementAtOrDefault(index);
        if (item is null) throw new InvalidOperationException("STAY_ITEM_INDEX_STALE");
        _ = await SendAsync(new RemoveStayItemCommand(stayId, index, item.ItemId));
    }

    public async Task TransferAsync(string stayId) =>
        _ = await SendAsync(new TransferStayRoomCommand(stayId));

    public async Task CheckoutAsync(string stayId) =>
        _ = await SendAsync(new CheckoutStayCommand(stayId));

    public async Task<BoardingStay> CreateStayAsync(NewStayRequest request)
    {
        // A request without a patient file is a manual walk-in: carry the typed identity so the
        // backend can find-or-create the owner and pet instead of silently dropping it.
        var walkIn = string.IsNullOrWhiteSpace(request.PatientId)
            ? new WalkInBoarder(request.PetName, BoardingMapping.PetSpecies(request.Species), request.Breed,
                request.OwnerName, request.OwnerPhone)
            : null;
        var id = await SendAsync(new CreateBoardingStayCommand(request.PatientId, request.Room,
            request.CheckIn, request.CheckOut, request.DailyRate, request.Note,
            request.PaymentMode == BoardingPaymentMode.Prepaid ? StayPaymentMode.Prepaid : StayPaymentMode.Postpaid,
            walkIn));
        return (await GetDetailAsync(id))?.Stay ?? throw new InvalidOperationException("STAY_NOT_FOUND");
    }

    public async Task<IReadOnlyList<RoomInfo>> GetRoomsAsync() =>
        (await QueryAsync(new BoardingRoomsQuery())).Select(r => r.ToContract()).ToArray();

    public async Task<IReadOnlyList<ServiceInfo>> GetServicesAsync() =>
        (await QueryAsync(new BoardingServicesQuery()))
            .Select(r => new ServiceInfo(r.Id, r.Title, r.UnitPrice.Minor)).ToArray();

    public async Task<IReadOnlyList<BoardingSelectableItem>> GetSelectableItemsAsync() =>
        (await QueryAsync(new BoardingSelectableItemsQuery())).Select(r => r.ToContract()).ToArray();

    public async Task<IReadOnlyList<BoardingKnownPet>> GetKnownPetsAsync() =>
        (await QueryAsync(new BoardingKnownPetsQuery())).Select(r => r.ToContract()).ToArray();

    public async Task<BoardingHistoryPage> GetHistoryAsync(BoardingHistoryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var (_, zone) = Calendar(sp);
        var kinds = filter.Kinds;
        var all = kinds.Count == 0;
        var criteria = new BoardingHistoryCriteria(filter.Search, filter.Sort.ToString(), filter.Page,
            filter.PageSize,
            ClinicDayWindow.ParseJalali(filter.FromDate, zone, endOfDay: false),
            ClinicDayWindow.ParseJalali(filter.ToDate, zone, endOfDay: true),
            IncludeCheckIn: all || kinds.Contains(BoardingHistoryKind.CheckIn),
            IncludeCheckOut: all || kinds.Contains(BoardingHistoryKind.CheckOut),
            Species: filter.Species is { } species ? BoardingMapping.PetSpeciesFilter(species) : null);
        var history = await QueryAsync(new BoardingHistoryQuery(criteria), sp);
        return new BoardingHistoryPage(history.Items.Select(r => r.ToContract(zone)).ToArray(),
            history.TotalCount, history.InflowMinor, history.OutflowMinor);
    }
}
