using Animora.Domain.Boarding;
using Animora.Domain.Patients;

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

/// <summary>
/// The boarding-history filter, applied in SQL before paging so the summary totals always describe the
/// filtered set. Null/empty members mean "unbounded".
/// </summary>
/// <param name="Search">Free text matched against pet / owner / room.</param>
/// <param name="Sort">Newest | Oldest | AmountDesc | AmountAsc.</param>
/// <param name="Page">1-based page.</param>
/// <param name="PageSize">Rows per page.</param>
/// <param name="FromUtc">Inclusive lower bound on the event instant.</param>
/// <param name="ToUtc">Inclusive upper bound on the event instant.</param>
/// <param name="IncludeCheckIn">Whether check-in rows (active stays) are wanted.</param>
/// <param name="IncludeCheckOut">Whether check-out rows (finished stays) are wanted.</param>
/// <param name="Species">Restrict to these pet species; null = all.</param>
public sealed record BoardingHistoryCriteria(string? Search, string Sort, int Page, int PageSize,
    DateTimeOffset? FromUtc = null, DateTimeOffset? ToUtc = null, bool IncludeCheckIn = true,
    bool IncludeCheckOut = true, IReadOnlyList<PetSpecies>? Species = null);
