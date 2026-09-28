using Animora.Domain.Boarding;
using Animora.Domain.Patients;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

public sealed class BoardingQueryTests
{
    [Fact]
    public async Task Rooms_UseOneSelect_AndCountOnlyActiveStays()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var room = Room.Create(fixture.TenantId, "R-01", "Standard", RoomType.Standard, 2).Value!;
        var empty = Room.Create(fixture.TenantId, "R-02", "Empty", RoomType.Standard, 1).Value!;
        var owner = Owner.Register(fixture.TenantId, "Owner", "09120000000", fixture.Clock);
        var pet = Pet.Register(fixture.TenantId, owner.Id, "Pet", PetSpecies.Dog, fixture.Clock);
        db.AddRange(room, empty, owner, pet);
        var active = Stay(fixture, room.Id, pet.Id, owner.Id, 1);
        var ready = Stay(fixture, room.Id, pet.Id, owner.Id, 2);
        ready.RequestCheckout(fixture.Clock);
        db.AddRange(active, ready);
        await db.SaveChangesAsync();

        var sql = db.Set<Room>().AsNoTracking().OrderBy(r => r.Code)
            .Select(r => new { r.Id, Occupied = db.Set<BoardingStay>()
                .Count(s => s.RoomId == r.Id && s.State == StayState.Active) }).ToQueryString();
        Assert.Contains("COUNT(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("; SELECT", sql, StringComparison.OrdinalIgnoreCase);
        var (counted, counter) = fixture.CreateCountingContext();
        using (counted)
        {
            var rows = await new BoardingQueries(counted).GetRoomsAsync(default);
            Assert.Equal(1, counter.Count);
            Assert.Equal(1, rows.Single(r => r.Room.Id == room.Id).Occupied);
            Assert.Equal(0, rows.Single(r => r.Room.Id == empty.Id).Occupied);
        }
    }

    [Fact]
    public async Task History_RespectsSortAndCapsPageSizeAtTwoHundred()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var owner = Owner.Register(fixture.TenantId, "Owner", "09120000000", fixture.Clock);
        var pet = Pet.Register(fixture.TenantId, owner.Id, "Pet", PetSpecies.Dog, fixture.Clock);
        var room = Room.Create(fixture.TenantId, "R-01", "Standard", RoomType.Standard, 1).Value!;
        db.AddRange(owner, pet, room);
        for (var i = 0; i < 205; i++)
        {
            var from = fixture.Clock.UtcNow.AddDays(-i - 1);
            var stay = BoardingStay.Reserve(fixture.TenantId, pet.Id, owner.Id, room.Id,
                from, from.AddDays(1), Money.Irt(100)).Value!;
            stay.Activate(fixture.Clock);
            db.Add(stay);
        }
        await db.SaveChangesAsync();

        var query = new BoardingQueries(db);
        var oldest = await query.GetHistoryAsync(null, "Oldest", 1, 500, default);
        var newest = await query.GetHistoryAsync(null, "Newest", 1, 500, default);
        Assert.Equal(205, oldest.TotalCount);
        Assert.Equal(200, oldest.Items.Count);
        Assert.Equal(200, newest.Items.Count);
        Assert.True(oldest.Items[0].OccurredUtc < newest.Items[0].OccurredUtc);
        Assert.Equal(5, (await query.GetHistoryAsync(null, "Newest", 2, 500, default)).Items.Count);
    }

    private static BoardingStay Stay(LocalStoreTestFixture fixture, string roomId, string petId, string ownerId, int daysAgo)
    {
        var from = fixture.Clock.UtcNow.AddDays(-daysAgo);
        var stay = BoardingStay.Reserve(fixture.TenantId, petId, ownerId, roomId, from,
            from.AddDays(3), Money.Irt(100)).Value!;
        stay.Activate(fixture.Clock);
        return stay;
    }
}
