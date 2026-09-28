using Animora.Domain.Boarding;

namespace Animora.Application.UseCases.Boarding.ReadModels;

public sealed record StayListItemReadModel(BoardingStay Stay, string PetName, string Species,
    string Breed, string OwnerName, string OwnerPhone, string RoomLabel);

public sealed record StayDetailReadModel(StayListItemReadModel Header, IReadOnlyList<StayItem> Items);

public sealed record RoomOccupancyReadModel(Room Room, int Occupied);

public sealed record BoardingSelectableReadModel(string Id, string Title, long PriceMinor, string Source);

public sealed record BoardingKnownPetReadModel(string Id, string Name, string Species, string Breed,
    string OwnerName, string OwnerPhone, string OwnerId);

public sealed record BoardingHistoryRowReadModel(string Id, string StayId, string PetName,
    string Species, string OwnerName, string RoomLabel, DateTimeOffset OccurredUtc,
    string Kind, long AmountMinor, string ActorId);

public sealed record BoardingHistoryResult(IReadOnlyList<BoardingHistoryRowReadModel> Items,
    int TotalCount, long InflowMinor, long OutflowMinor);
