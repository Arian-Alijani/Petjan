using System.Text.Json;
using Animora.Domain.Boarding;
using Animora.Domain.Patients;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Seed;

/// <summary>Optional example stays, guarded by a marker and existing patient records.</summary>
public sealed class BoardingSampleData(AnimoraLocalDbContext db, ILocalTenantContext tenant, IClock clock)
{
    public const string SampleKey = "boarding.sample";

    public bool AlreadySeeded() => db.Set<BoardingStay>().IgnoreQueryFilters().Any();

    public void Populate()
    {
        var pets = db.Set<Pet>().AsNoTracking().OrderBy(p => p.Id).Take(14).ToArray();
        var rooms = db.Set<Room>().AsNoTracking().OrderBy(r => r.Code).Take(8).ToArray();
        var services = db.Set<BoardingServiceDef>().AsNoTracking().OrderBy(s => s.Code).Take(6).ToArray();
        if (pets.Length < 14 || rooms.Length < 5 || services.Length == 0) return;

        for (var i = 0; i < 14; i++)
        {
            var now = clock.UtcNow;
            var checkIn = new DateTimeOffset(now.UtcDateTime.Date.AddDays(i < 5 ? -1 : -20 - i), TimeSpan.Zero);
            var checkOut = checkIn.AddDays(4);
            var pet = pets[i];
            var stay = BoardingStay.Reserve(tenant.TenantId, pet.Id, pet.OwnerId,
                rooms[i % 5].Id, checkIn, checkOut, Money.Irt(250_000)).Value!;
            var fixedClock = new FixedClock(i < 5 ? now : checkOut);
            stay.Activate(fixedClock);
            var service = services[i % services.Length];
            stay.AddItem(new StayItem($"sample-item-{i:D2}", StayItemKind.BoardingService,
                service.Id, service.Title, new Quantity(1, "PCS"), service.UnitPrice,
                checkIn.AddHours(2), "sample"));
            if (i >= 5)
            {
                stay.RequestCheckout(fixedClock);
                var total = StayChargeCalculator.Accrued(stay, services, checkOut, TimeZoneInfo.Utc);
                stay.FreezeTotalForCheckout(total);
                db.Add(BoardingBillingHandoff.Create(tenant.TenantId, stay.Id, pet.OwnerId, pet.Id,
                    checkOut, JsonSerializer.Serialize(new { TotalMinor = total.Minor,
                        Currency = total.Currency, stay.Items })));
            }
            db.Add(stay);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
