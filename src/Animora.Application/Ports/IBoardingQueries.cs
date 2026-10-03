using Animora.Application.UseCases.Boarding.ReadModels;
using Animora.Domain.Boarding;

namespace Animora.Application.Ports;

public interface IBoardingQueries
{
    Task<IReadOnlyList<StayListItemReadModel>> GetStaysAsync(CancellationToken ct);
    Task<StayDetailReadModel?> GetDetailAsync(string stayId, CancellationToken ct);
    Task<IReadOnlyList<RoomOccupancyReadModel>> GetRoomsAsync(CancellationToken ct);
    Task<Room?> FindRoomAsync(string idOrCode, CancellationToken ct);
    Task<int> CountActiveStaysInRoomAsync(string roomId, CancellationToken ct);
    Task<IReadOnlyList<BoardingServiceDef>> GetServicesAsync(CancellationToken ct);
    Task<BoardingSelectableReadModel?> FindSelectableAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<BoardingSelectableReadModel>> GetSelectableAsync(CancellationToken ct);
    Task<IReadOnlyList<BoardingKnownPetReadModel>> GetKnownPetsAsync(string? search, CancellationToken ct);
    Task<BoardingKnownPetReadModel?> FindPetAsync(string petId, CancellationToken ct);
    Task<BoardingHistoryResult> GetHistoryAsync(BoardingHistoryCriteria criteria, CancellationToken ct);
}
