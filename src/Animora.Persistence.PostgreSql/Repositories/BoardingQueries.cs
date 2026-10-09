using Animora.Application.Ports;
using Animora.Application.Text;
using Animora.Application.UseCases.Boarding.ReadModels;
using Animora.Domain.Boarding;
using Animora.Domain.Patients;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Boarding;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

/// <summary>Tenant-filtered boarding projections; occupancy is counted in SQL, not per room.</summary>
public sealed class BoardingQueries(AnimoraServerDbContext db) : IBoardingQueries
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
        // Management shows only stays still on the floor. Presence (not billing state) is the signal:
        // ActualCheckOutUtc is stamped only when a stay is truly checked out, so a prepaid stay that is
        // already invoiced but still boarding stays visible. Departed stays live in the history tab.
        var stays = db.Set<BoardingStay>().AsNoTracking()
            .Where(s => s.ActualCheckOutUtc == null && s.State != StayState.Cancelled)
            .OrderByDescending(s => s.CheckInUtc).ThenBy(s => s.Id).Take(200);
        return await Project(stays).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<StayDetailReadModel?> GetDetailAsync(string stayId, CancellationToken ct)
    {
        var stay = db.Set<BoardingStay>().AsNoTracking().Where(s => s.Id == stayId);
        var row = await Project(stay).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return row is null ? null : new StayDetailReadModel(row,
            row.Stay.Items.OrderBy(i => i.ConsumedUtc).ThenBy(i => i.ItemId, StringComparer.Ordinal).ToArray());
    }

    public async Task<IReadOnlyList<RoomOccupancyReadModel>> GetRoomsAsync(CancellationToken ct) =>
        await db.Set<Room>().AsNoTracking().OrderBy(r => r.Code)
            .Select(room => new RoomOccupancyReadModel(room,
                db.Set<BoardingStay>().Count(stay => stay.RoomId == room.Id && stay.ActualCheckOutUtc == null && stay.State != StayState.Cancelled)))
            .Take(PageRules.MaxPageSize).ToListAsync(ct).ConfigureAwait(false);

    public Task<Room?> FindRoomAsync(string idOrCode, CancellationToken ct) =>
        db.Set<Room>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == idOrCode || r.Code == idOrCode, ct);

    public Task<bool> HasReadyHandoffAsync(string stayId, CancellationToken ct) =>
        db.Set<BoardingBillingHandoff>().AsNoTracking()
            .AnyAsync(h => h.StayId == stayId && h.Status == 0, ct);

    public Task<int> CountActiveStaysInRoomAsync(string roomId, CancellationToken ct) =>
        db.Set<BoardingStay>().CountAsync(s => s.RoomId == roomId && s.ActualCheckOutUtc == null && s.State != StayState.Cancelled, ct);

    public async Task<IReadOnlyList<BoardingServiceDef>> GetServicesAsync(CancellationToken ct) =>
        await db.Set<BoardingServiceDef>().AsNoTracking().Where(s => s.IsActive)
            .OrderBy(s => s.Code).Take(200).ToListAsync(ct).ConfigureAwait(false);

    // Resolves the id the shared sales catalog hands out (SalesQueries.GetCatalogAsync): a boarding service id, a
    // VariantId (price = the variant's own SalePrice — never Product.DefaultSalePrice, which is 0 for multi-variant
    // goods) or, for products with no active variant, the product id with its default price.
    public async Task<BoardingSelectableReadModel?> FindSelectableAsync(string id, CancellationToken ct)
    {
        var boarding = await db.Set<BoardingServiceDef>().AsNoTracking().Where(s => s.Id == id && s.IsActive)
            .Select(s => new BoardingSelectableReadModel(s.Id, s.Title, s.UnitPrice.Minor, "BoardingService"))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (boarding is not null) return boarding;
        // Product/variant catalog storage is introduced by S10; absent data must never produce a guessed price.
        return null;
    }

    public async Task<IReadOnlyList<BoardingKnownPetReadModel>> GetKnownPetsAsync(string? search, CancellationToken ct)
    {
        var pets = db.Set<Pet>().AsNoTracking().AsQueryable();
        var owners = db.Set<Owner>().AsNoTracking().AsQueryable();
        string? term = null;
        if (!string.IsNullOrWhiteSpace(search))
        {
            // SearchKey columns are stored normalized (Persian/Arabic folding, digits); fold the term the same way
            // and match anywhere in the pet key (name/breed/chip) or the owner key (name/phone/national id).
            term = "%" + PersianText.Normalize(search) + "%";
        }
        return await (from pet in pets
            join owner in owners on pet.OwnerId equals owner.Id
            where term == null || EF.Functions.Like(pet.SearchKey, term) || EF.Functions.Like(owner.SearchKey, term)
            orderby pet.Name, pet.Id
            select new BoardingKnownPetReadModel(pet.Id, pet.Name, pet.Species.ToString(),
                pet.Breed ?? "", owner.FullName, owner.PhonePrimary, owner.Id)).Take(200).ToListAsync(ct).ConfigureAwait(false);
    }

    public Task<BoardingKnownPetReadModel?> FindPetAsync(string petId, CancellationToken ct) =>
        (from pet in db.Set<Pet>().AsNoTracking()
         join owner in db.Set<Owner>().AsNoTracking() on pet.OwnerId equals owner.Id
         where pet.Id == petId
         select new BoardingKnownPetReadModel(pet.Id, pet.Name, pet.Species.ToString(),
             pet.Breed ?? "", owner.FullName, owner.PhonePrimary, owner.Id))
        .FirstOrDefaultAsync(ct).ConfigureAwait(false);

    public async Task<BoardingHistoryResult> GetHistoryAsync(BoardingHistoryCriteria criteria, CancellationToken ct)
    {
        // Every filter is applied on the entity source so EF never has to translate a predicate or a
        // sort key that reaches into the constructed read model (r.Stay.State would fail translation).
        var stays = db.Set<BoardingStay>().AsNoTracking().Where(s => s.State != StayState.Reserved);

        // History rows carry two kinds today: CheckIn (stay still active) and CheckOut (finished).
        if (!criteria.IncludeCheckIn && !criteria.IncludeCheckOut) return Empty();
        if (!criteria.IncludeCheckIn) stays = stays.Where(s => s.State != StayState.Active);
        if (!criteria.IncludeCheckOut) stays = stays.Where(s => s.State == StayState.Active);

        if (criteria.FromUtc is { } from) stays = stays.Where(s => s.CheckInUtc >= from);
        if (criteria.ToUtc is { } to) stays = stays.Where(s => s.CheckInUtc <= to);

        if (criteria.Species is { Count: > 0 } species)
        {
            var wanted = species.ToArray();
            stays = stays.Where(s => db.Set<Pet>().Any(p => p.Id == s.PetId && wanted.Contains(p.Species)));
        }

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var term = "%" + criteria.Search.Trim() + "%";
            stays = stays.Where(s =>
                db.Set<Pet>().Any(p => p.Id == s.PetId && EF.Functions.Like(p.Name, term)) ||
                db.Set<Owner>().Any(o => o.Id == s.OwnerId && EF.Functions.Like(o.FullName, term)) ||
                db.Set<Room>().Any(r => r.Id == s.RoomId && EF.Functions.Like(r.Code, term)));
        }

        // Summary totals describe the whole filtered set, not just the visible page. A stay's recorded
        // amount (frozen checkout total) is money charged to the owner, i.e. inflow; nothing flows out.
        var total = await stays.CountAsync(ct).ConfigureAwait(false);
        var inflow = await stays.SumAsync(s => s.FrozenTotalMinor ?? 0L, ct);

        // The room rate is the persisted price snapshot; totals are derived in the handler.
        var ordered = criteria.Sort switch
        {
            "Oldest" => stays.OrderBy(s => s.CheckInUtc).ThenBy(s => s.Id),
            "AmountAsc" => stays.OrderBy(s => s.DailyRate.Minor).ThenBy(s => s.Id),
            "AmountDesc" => stays.OrderByDescending(s => s.DailyRate.Minor).ThenBy(s => s.Id),
            _ => stays.OrderByDescending(s => s.CheckInUtc).ThenBy(s => s.Id),
        };
        var pageSize = Math.Clamp(criteria.PageSize, 1, 200);
        var rows = await Project(ordered.Skip((Math.Max(1, criteria.Page) - 1) * pageSize).Take(pageSize))
            .ToListAsync(ct).ConfigureAwait(false);
        return new BoardingHistoryResult(rows.Select(r => new BoardingHistoryRowReadModel(
            r.Stay.Id, r.Stay.Id, r.PetName, r.Species, r.OwnerName, r.RoomLabel,
            r.Stay.CheckInUtc, r.Stay.State == StayState.Active ? "CheckIn" : "CheckOut",
            r.Stay.FrozenTotalMinor ?? 0, "")).ToArray(), total, inflow, 0);
    }

    private static BoardingHistoryResult Empty() => new([], 0, 0, 0);
}
