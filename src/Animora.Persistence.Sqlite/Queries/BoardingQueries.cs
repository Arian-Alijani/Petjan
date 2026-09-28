using Animora.Application.Ports;
using Animora.Application.UseCases.Boarding.ReadModels;
using Animora.Domain.Boarding;
using Animora.Domain.Inventory;
using Animora.Domain.Patients;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>Tenant-filtered boarding projections; occupancy is counted in SQL, not per room.</summary>
public sealed class BoardingQueries(AnimoraLocalDbContext db) : IBoardingQueries
{
    // Project an already-filtered/ordered stay query into the list read model. Filtering, ordering
    // and paging must be applied on the <see cref="BoardingStay"/> source before this projection,
    // because EF cannot translate predicates/sorts that reach into the constructed read model
    // (e.g. r.Stay.State) once the entity has been embedded in a non-entity type.
    private IQueryable<StayListItemReadModel> Project(IQueryable<BoardingStay> stays) =>
        from stay in stays
        join pet in db.Set<Pet>().AsNoTracking() on stay.PetId equals pet.Id
        join owner in db.Set<Owner>().AsNoTracking() on stay.OwnerId equals owner.Id
        join room in db.Set<Room>().AsNoTracking() on stay.RoomId equals room.Id
        select new StayListItemReadModel(stay, pet.Name, pet.Species.ToString(), pet.Breed ?? "",
            owner.FullName, owner.PhonePrimary, room.Code);

    public async Task<IReadOnlyList<StayListItemReadModel>> GetStaysAsync(CancellationToken ct)
    {
        var stays = db.Set<BoardingStay>().AsNoTracking()
            .OrderByDescending(s => s.CheckInUtc).ThenBy(s => s.Id).Take(200);
        return await Project(stays).ToListAsync(ct);
    }

    public async Task<StayDetailReadModel?> GetDetailAsync(string stayId, CancellationToken ct)
    {
        var stay = db.Set<BoardingStay>().AsNoTracking().Where(s => s.Id == stayId);
        var row = await Project(stay).FirstOrDefaultAsync(ct);
        return row is null ? null : new StayDetailReadModel(row,
            row.Stay.Items.OrderBy(i => i.ConsumedUtc).ThenBy(i => i.ItemId, StringComparer.Ordinal).ToArray());
    }

    public async Task<IReadOnlyList<RoomOccupancyReadModel>> GetRoomsAsync(CancellationToken ct) =>
        await db.Set<Room>().AsNoTracking().OrderBy(r => r.Code)
            .Select(room => new RoomOccupancyReadModel(room,
                db.Set<BoardingStay>().Count(stay => stay.RoomId == room.Id && stay.State == StayState.Active)))
            .ToListAsync(ct);

    public Task<Room?> FindRoomAsync(string idOrCode, CancellationToken ct) =>
        db.Set<Room>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == idOrCode || r.Code == idOrCode, ct);

    public Task<int> CountActiveStaysInRoomAsync(string roomId, CancellationToken ct) =>
        db.Set<BoardingStay>().CountAsync(s => s.RoomId == roomId && s.State == StayState.Active, ct);

    public async Task<IReadOnlyList<BoardingServiceDef>> GetServicesAsync(CancellationToken ct) =>
        await db.Set<BoardingServiceDef>().AsNoTracking().Where(s => s.IsActive)
            .OrderBy(s => s.Code).Take(200).ToListAsync(ct);

    public async Task<IReadOnlyList<BoardingSelectableReadModel>> GetSelectableAsync(CancellationToken ct)
    {
        var boarding = db.Set<BoardingServiceDef>().AsNoTracking().Where(s => s.IsActive)
            .Select(s => new BoardingSelectableReadModel(s.Id, s.Title, s.UnitPrice.Minor, "BoardingService"));
        var products = db.Set<Product>().AsNoTracking().Where(p => p.IsActive &&
            (p.Kind == ProductKind.Service || p.Kind == ProductKind.Consumable || p.Kind == ProductKind.Medicine))
            .Select(p => new BoardingSelectableReadModel(p.Id, p.Name, p.DefaultSalePrice.Minor,
                p.Kind == ProductKind.Service ? "ClinicService" : "ClinicItem"));
        return await boarding.Concat(products).OrderBy(r => r.Title).ThenBy(r => r.Id).Take(500).ToListAsync(ct);
    }

    public async Task<BoardingSelectableReadModel?> FindSelectableAsync(string id, CancellationToken ct)
    {
        var boarding = await db.Set<BoardingServiceDef>().AsNoTracking().Where(s => s.Id == id && s.IsActive)
            .Select(s => new BoardingSelectableReadModel(s.Id, s.Title, s.UnitPrice.Minor, "BoardingService"))
            .FirstOrDefaultAsync(ct);
        if (boarding is not null) return boarding;
        return await db.Set<Product>().AsNoTracking().Where(p => p.Id == id && p.IsActive &&
            (p.Kind == ProductKind.Service || p.Kind == ProductKind.Consumable || p.Kind == ProductKind.Medicine))
            .Select(p => new BoardingSelectableReadModel(p.Id, p.Name, p.DefaultSalePrice.Minor,
                p.Kind == ProductKind.Service ? "ClinicService" : "ClinicItem")).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<BoardingKnownPetReadModel>> GetKnownPetsAsync(string? search, CancellationToken ct)
    {
        var pets = db.Set<Pet>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            pets = pets.Where(p => EF.Functions.Like(p.SearchKey, term + "%"));
        }
        return await (from pet in pets
            join owner in db.Set<Owner>().AsNoTracking() on pet.OwnerId equals owner.Id
            orderby pet.Name, pet.Id
            select new BoardingKnownPetReadModel(pet.Id, pet.Name, pet.Species.ToString(),
                pet.Breed ?? "", owner.FullName, owner.PhonePrimary, owner.Id)).Take(200).ToListAsync(ct);
    }

    public Task<BoardingKnownPetReadModel?> FindPetAsync(string petId, CancellationToken ct) =>
        (from pet in db.Set<Pet>().AsNoTracking()
         join owner in db.Set<Owner>().AsNoTracking() on pet.OwnerId equals owner.Id
         where pet.Id == petId
         select new BoardingKnownPetReadModel(pet.Id, pet.Name, pet.Species.ToString(),
             pet.Breed ?? "", owner.FullName, owner.PhonePrimary, owner.Id))
        .FirstOrDefaultAsync(ct);

    public async Task<BoardingHistoryResult> GetHistoryAsync(
        string? search, string sort, int page, int pageSize, CancellationToken ct)
    {
        // Filter and sort on the entity source first so EF never has to translate a predicate or a
        // sort key that reaches into the constructed read model (r.Stay.State / r.Stay.CheckInUtc
        // would both fail translation once BoardingStay is embedded in a non-entity type).
        var stays = db.Set<BoardingStay>().AsNoTracking().Where(s => s.State != StayState.Reserved);
        // The room rate is the persisted price snapshot; totals are derived in the handler.
        stays = sort switch
        {
            "Oldest" => stays.OrderBy(s => s.CheckInUtc).ThenBy(s => s.Id),
            "AmountAsc" => stays.OrderBy(s => s.DailyRate.Minor).ThenBy(s => s.Id),
            "AmountDesc" => stays.OrderByDescending(s => s.DailyRate.Minor).ThenBy(s => s.Id),
            _ => stays.OrderByDescending(s => s.CheckInUtc).ThenBy(s => s.Id),
        };
        // The name filter needs the joined pet/owner/room labels, so it is applied on the projection;
        // it only touches the read model's own top-level members, which EF can translate.
        var query = Project(stays);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = "%" + search.Trim() + "%";
            query = query.Where(r => EF.Functions.Like(r.PetName, term) ||
                EF.Functions.Like(r.OwnerName, term) || EF.Functions.Like(r.RoomLabel, term));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((Math.Max(1, page) - 1) * Math.Clamp(pageSize, 1, 200))
            .Take(Math.Clamp(pageSize, 1, 200)).ToListAsync(ct);
        return new BoardingHistoryResult(rows.Select(r => new BoardingHistoryRowReadModel(
            r.Stay.Id, r.Stay.Id, r.PetName, r.Species, r.OwnerName, r.RoomLabel,
            r.Stay.CheckInUtc, r.Stay.State == StayState.Active ? "CheckIn" : "CheckOut",
            r.Stay.FrozenTotalMinor ?? 0, "")).ToArray(), total, 0, 0);
    }
}
