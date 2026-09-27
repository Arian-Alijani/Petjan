using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Scheduling;
using Animora.Domain.Time;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Seed;

/// <summary>Optional first-run reception fixtures. Marker-guarded in LocalSeeder; never creates credentials.</summary>
public sealed class ReceptionSampleData(AnimoraLocalDbContext db, ILocalTenantContext tenant, IClock clock)
{
    public const string SampleKey = "reception.sample";

    public bool AlreadySeeded() => db.Set<Appointment>().IgnoreQueryFilters().Any()
        || db.Set<ReceptionQueueEntry>().IgnoreQueryFilters().Any();

    public void Populate()
    {
        // PatientsSampleData runs first. Never invent a queue entry without a real patient/owner.
        var pets = db.Set<Pet>().AsNoTracking().OrderBy(p => p.Id).Take(6).ToArray();
        if (pets.Length < 6) return;

        var day = clock.UtcNow.UtcDateTime.Date;
        var seed = new DateTimeOffset(2026, 1, 2, 8, 0, 0, TimeSpan.Zero);
        var doctors = new List<User>();
        for (var i = 0; i < 4; i++)
        {
            var user = User.Create(tenant.TenantId, $"sample-doctor-{i + 1}",
                $"Sample Doctor {i + 1}", new FixedClock(seed.AddMinutes(i)));
            doctors.Add(user);
            db.Add(user);
            db.Add(StaffMembership.Create(tenant.TenantId, user.Id, StaffRole.Doctor,
                new FixedClock(seed.AddMinutes(i))));
        }

        for (var i = 0; i < 4; i++)
        {
            var at = new DateTimeOffset(day.AddHours(9 + i), TimeSpan.Zero);
            var appointment = Appointment.Book(tenant.TenantId, pets[i].Id, pets[i].OwnerId,
                "CHECKUP", at, 30, AppointmentSource.Phone, doctors[i % doctors.Count].Id,
                new FixedClock(seed.AddHours(i + 1)), doctors[i % doctors.Count].Id);
            if (appointment.IsFailure) throw new InvalidOperationException(appointment.Error.Code);
            if (i == 3) appointment.Value!.MarkNoShow(new FixedClock(at));
            db.Add(appointment.Value!);
        }
        for (var i = 0; i < 2; i++)
        {
            var at = new DateTimeOffset(day.AddHours(10 + i).AddMinutes(15), TimeSpan.Zero);
            db.Add(ReceptionQueueEntry.Create(tenant.TenantId, pets[i + 4].Id, pets[i + 4].OwnerId,
                QueueSource.WalkIn, i + 1, new FixedClock(at)));
        }
    }

    private sealed class FixedClock(DateTimeOffset time) : IClock
    {
        public DateTimeOffset UtcNow => time;
    }
}
