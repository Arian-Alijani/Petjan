using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.UseCases.Boarding;
using Animora.Application.UseCases.Boarding.ReadModels;
using Animora.Domain.Boarding;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Application.Tests;

public sealed class BoardingUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly CommandContext Context = new("T-LOCAL-DEFAULT", "user", "device", "operation", "correlation", null, null, null, Now);
    private static readonly IClock Clock = new BoardingClock(Now);

    private static BoardingStay Active(string roomId)
    {
        var stay = BoardingStay.Reserve(Context.TenantId, "pet", "owner", roomId,
            Now.AddDays(-2), Now.AddDays(1), Money.Irt(100)).Value!;
        Assert.True(stay.Activate(Clock).IsSuccess);
        return stay;
    }

    [Fact]
    public async Task TransferToFullRoom_DoesNotMoveStay()
    {
        var source = Room.Create(Context.TenantId, "R-1", "Source", RoomType.Standard, 1).Value!;
        var full = Room.Create(Context.TenantId, "R-2", "Full", RoomType.Standard, 1).Value!;
        var stay = Active(source.Id);
        var repository = new BoardingRepositoryFake(stay);
        var queries = new BoardingQueriesFake(source, full) { OccupiedRoomId = full.Id };

        var result = await new TransferStayRoomCommandHandler(repository, queries, Clock)
            .HandleAsync(new TransferStayRoomCommand(stay.Id, full.Id), Context, default);

        Assert.Equal(ErrorCodes.RoomAtCapacity, result.Error);
        Assert.Equal(source.Id, stay.RoomId);
        Assert.Single(stay.RoomHistory);
    }

    [Fact]
    public async Task StaleIndex_DoesNotRemoveAnyItem()
    {
        var stay = Active("room");
        stay.AddItem(new StayItem("first", StayItemKind.Custom, "a", "A", new Quantity(1, "PCS"),
            Money.Irt(10), Now.AddHours(-1), "user"));
        stay.AddItem(new StayItem("second", StayItemKind.Custom, "b", "B", new Quantity(1, "PCS"),
            Money.Irt(20), Now, "user"));

        var result = await new RemoveStayItemCommandHandler(new BoardingRepositoryFake(stay))
            .HandleAsync(new RemoveStayItemCommand(stay.Id, 0, "second"), Context, default);

        Assert.Equal(ErrorCodes.StayItemIndexStale, result.Error);
        Assert.Equal(new[] { "first", "second" }, stay.Items.Select(i => i.ItemId));
    }

    [Fact]
    public async Task Checkout_CreatesOneHandoff_AndReleasesOccupancy()
    {
        var room = Room.Create(Context.TenantId, "R-1", "Room", RoomType.Standard, 1).Value!;
        var stay = Active(room.Id);
        var queries = new BoardingQueriesFake(room) { ActiveStay = stay };
        var handoffs = new BoardingHandoffFake();
        var handler = new CheckoutStayCommandHandler(new BoardingRepositoryFake(stay), queries,
            handoffs, new TestZone(), Clock);

        Assert.Equal(1, await queries.CountActiveStaysInRoomAsync(room.Id, default));
        var first = await handler.HandleAsync(new CheckoutStayCommand(stay.Id), Context, default);
        Assert.True(first.IsSuccess);
        Assert.Equal(StayState.ReadyForInvoice, stay.State);
        Assert.Equal(200, stay.FrozenTotalMinor);
        Assert.Equal(0, await queries.CountActiveStaysInRoomAsync(room.Id, default));
        Assert.Single(handoffs.StayIds);
        Assert.Equal(stay.Id, handoffs.StayIds[0]);

        var second = await handler.HandleAsync(new CheckoutStayCommand(stay.Id), Context, default);
        Assert.Equal(ErrorCodes.StayAlreadyCheckedOut, second.Error);
        Assert.Single(handoffs.StayIds);
    }
}

internal sealed class BoardingClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow => now;
}

internal sealed class BoardingRepositoryFake(params BoardingStay[] stays) : IBoardingRepository
{
    private readonly Dictionary<string, BoardingStay> _stays = stays.ToDictionary(s => s.Id);
    public Task<BoardingStay?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult(_stays.GetValueOrDefault(id));
    public Task<BoardingStay> RequireAsync(string id, CancellationToken ct = default) => Task.FromResult(_stays[id]);
    public void Add(BoardingStay stay) => _stays.Add(stay.Id, stay);
    public void AddRange(IEnumerable<BoardingStay> stays)
    {
        foreach (var stay in stays) Add(stay);
    }
}

internal sealed class BoardingHandoffFake : IBoardingBillingHandoffWriter
{
    public List<string> StayIds { get; } = [];
    public void Add(string tenantId, string stayId, string ownerId, string petId,
        DateTimeOffset completedUtc, string linesJson) => StayIds.Add(stayId);
}

internal sealed class BoardingQueriesFake(params Room[] rooms) : IBoardingQueries
{
    public string? OccupiedRoomId { get; set; }
    public BoardingStay? ActiveStay { get; set; }
    public Task<int> CountActiveStaysInRoomAsync(string roomId, CancellationToken ct) =>
        Task.FromResult((OccupiedRoomId == roomId ? 1 : 0) +
            (ActiveStay is { State: StayState.Active } stay && stay.RoomId == roomId ? 1 : 0));
    public Task<Room?> FindRoomAsync(string idOrCode, CancellationToken ct) =>
        Task.FromResult(rooms.FirstOrDefault(r => r.Id == idOrCode || r.Code == idOrCode));
    public Task<IReadOnlyList<RoomOccupancyReadModel>> GetRoomsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RoomOccupancyReadModel>>(rooms.Select(r =>
            new RoomOccupancyReadModel(r, CountActiveStaysInRoomAsync(r.Id, ct).Result)).ToArray());
    public Task<IReadOnlyList<BoardingServiceDef>> GetServicesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BoardingServiceDef>>([]);
    public Task<IReadOnlyList<StayListItemReadModel>> GetStaysAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StayListItemReadModel>>([]);
    public Task<StayDetailReadModel?> GetDetailAsync(string stayId, CancellationToken ct) =>
        Task.FromResult<StayDetailReadModel?>(null);
    public Task<BoardingSelectableReadModel?> FindSelectableAsync(string id, CancellationToken ct) =>
        Task.FromResult<BoardingSelectableReadModel?>(null);
    public Task<IReadOnlyList<BoardingSelectableReadModel>> GetSelectableAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BoardingSelectableReadModel>>([]);
    public Task<IReadOnlyList<BoardingKnownPetReadModel>> GetKnownPetsAsync(string? search, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BoardingKnownPetReadModel>>([]);
    public Task<BoardingKnownPetReadModel?> FindPetAsync(string petId, CancellationToken ct) =>
        Task.FromResult<BoardingKnownPetReadModel?>(null);
    public Task<BoardingHistoryResult> GetHistoryAsync(string? search, string sort, int page, int pageSize, CancellationToken ct) =>
        Task.FromResult(new BoardingHistoryResult([], 0, 0, 0));
}
