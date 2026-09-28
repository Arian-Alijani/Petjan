using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Scheduling;
using Animora.Domain.Time;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Seed;

/// <summary>Optional sample calendar, applied only after patients and reception doctors exist.</summary>
public sealed class SchedulingSampleData(AnimoraLocalDbContext db, ILocalTenantContext tenant, IClock clock)
{
    public const string SampleKey = "scheduling.sample";

    // Four appointments are produced by ReceptionSampleData. Never overwrite a populated calendar.
    public bool AlreadySeeded() => db.Set<Appointment>().IgnoreQueryFilters().Count() > 4;

    public void Populate()
    {
        var pets = db.Set<Pet>().AsNoTracking().OrderBy(p => p.Id).Take(18).ToArray();
        var doctors = db.Set<StaffMembership>().AsNoTracking()
            .Where(m => m.Role == StaffRole.Doctor && m.Status == MembershipStatus.Active)
            .OrderBy(m => m.UserId).Take(4).Select(m => m.UserId).ToArray();
        if (pets.Length < 18 || doctors.Length < 4) return;

        var today = clock.UtcNow.UtcDateTime.Date;
        var fixedClock = new FixedClock(new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero));
        for (var i = 0; i < 40; i++)
        {
            // Twenty days, two starts per day. First pair is deliberately adjacent for the same doctor.
            var offset = i / 2 - 7;
            if (offset >= 0) offset++; // Avoid the reception fixture's existing appointments today.
            var start = new DateTimeOffset(today.AddDays(offset).AddHours(8).AddMinutes((i % 2) * 30), TimeSpan.Zero);
            var pet = pets[i % pets.Length];
            var doctor = doctors[(i / 2) % doctors.Length];
            var booked = Appointment.Book(tenant.TenantId, pet.Id, pet.OwnerId, "CHECKUP", start, 30,
                AppointmentSource.Phone, doctor, fixedClock, doctor);
            if (booked.IsFailure) throw new InvalidOperationException(booked.Error.Code);
            var appointment = booked.Value!;
            if (offset < 0)
            {
                if (i % 4 == 0) appointment.MarkNoShow(fixedClock);
                else if (i % 4 == 1) appointment.Cancel("sample cancellation", fixedClock);
                else appointment.Confirm();
            }
            else if (i % 3 == 0) appointment.Confirm();
            db.Add(appointment);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
