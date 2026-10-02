using Animora.Application.Ports;
using Animora.Application.UseCases.Scheduling.ReadModels;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Scheduling;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>Tenant-filtered reads with half-open UTC boundaries and bounded history.</summary>
public sealed class AppointmentQueries(AnimoraLocalDbContext db) : IAppointmentQueries
{
    private IQueryable<AppointmentEventProjection> Events(IQueryable<Appointment> source) =>
        SchedulingProjections.AppointmentEvent(source, db);

    private static AppointmentEventReadModel Map(AppointmentEventProjection r) =>
        new(r.Id, r.StartUtc.UtcTicks, r.DurationMinutes, r.State, r.TypeCode, r.DoctorId,
            r.DoctorName, r.PetId, r.PetName, r.PetSpecies.ToString(), r.OwnerId,
            r.OwnerName, r.OwnerPhone, r.Note,
            r.VisitId, r.CheckedInUtc?.UtcTicks, r.CancelReason, r.CreatedByUserId, r.RescheduleCount,
            r.Source, r.InvoiceTotalMinor, r.InvoicePaidMinor, r.Currency, r.InvoiceState);

    private static DateTimeOffset Utc(long ticks) => new(ticks, TimeSpan.Zero);

    public async Task<IReadOnlyList<AppointmentEventReadModel>> GetRangeAsync(
        long startTicks, long endTicks, string? doctorId, CancellationToken ct)
    {
        var start = Utc(startTicks); var end = Utc(endTicks);
        var source = db.Set<Appointment>().AsNoTracking()
            .Where(a => a.StartUtc >= start && a.StartUtc < end);
        if (!string.IsNullOrEmpty(doctorId)) source = source.Where(a => a.DoctorUserId == doctorId);
        return (await Events(source).OrderBy(r => r.StartUtc).ThenBy(r => r.Id).ToListAsync(ct))
            .Select(Map).ToArray();
    }

    public async Task<IReadOnlyDictionary<long, int>> GetDayCountsRawAsync(long startTicks, long endTicks, CancellationToken ct)
    {
        var start = Utc(startTicks); var end = Utc(endTicks);
        // One indexed statement. Each UTC instant is bucketed into its local calendar day by the handler.
        var starts = await db.Set<Appointment>().AsNoTracking()
            .Where(a => a.StartUtc >= start && a.StartUtc < end && a.State != AppointmentState.Cancelled)
            .Select(a => a.StartUtc).ToListAsync(ct);
        return starts.GroupBy(d => d.UtcTicks).ToDictionary(g => g.Key, g => g.Count());
    }

    public Task<int> GetDoctorBusyCountAsync(string doctorId, long startTicks, long endTicks, CancellationToken ct)
    {
        var start = Utc(startTicks); var end = Utc(endTicks);
        return db.Set<Appointment>().AsNoTracking().CountAsync(a => a.DoctorUserId == doctorId &&
            a.StartUtc >= start && a.StartUtc < end && a.State != AppointmentState.Cancelled, ct);
    }

    public async Task<AppointmentDetailReadModel?> GetDetailAsync(string appointmentId, CancellationToken ct)
    {
        var row = await Events(db.Set<Appointment>().AsNoTracking().Where(a => a.Id == appointmentId))
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;
        // Creator display name is a real lookup; null (never a guess) when the user is unknown.
        var creator = string.IsNullOrEmpty(row.CreatedByUserId) ? null : await db.Set<User>().AsNoTracking()
            .Where(u => u.Id == row.CreatedByUserId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
        return new AppointmentDetailReadModel(Map(row), "", row.AggregateVersion, creator);
    }

    public async Task<IReadOnlyList<AppointmentEventReadModel>> GetQueueAsync(long startTicks, long endTicks, CancellationToken ct)
    {
        var start = Utc(startTicks); var end = Utc(endTicks);
        var rows = await Events(db.Set<Appointment>().AsNoTracking().Where(a =>
            a.StartUtc >= start && a.StartUtc < end && (a.State == AppointmentState.CheckedIn ||
                a.State == AppointmentState.Scheduled || a.State == AppointmentState.Confirmed)))
            .OrderBy(a => a.StartUtc).ToListAsync(ct);
        return rows.Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<AppointmentEventReadModel>> GetHistoryAsync(CancellationToken ct)
    {
        var rows = await Events(db.Set<Appointment>().AsNoTracking().Where(a =>
            a.State == AppointmentState.Completed || a.State == AppointmentState.NoShow ||
            a.State == AppointmentState.Cancelled)).OrderByDescending(a => a.StartUtc)
            .ThenBy(a => a.Id).Take(200).ToListAsync(ct);
        return rows.Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<ExistingSlot>> GetDoctorSlotsAsync(
        string doctorId, long startTicks, long endTicks, string? excludeId, CancellationToken ct)
    {
        var start = Utc(startTicks).AddMinutes(-Appointment.MaxDurationMinutes);
        var end = Utc(endTicks);
        var rows = await db.Set<Appointment>().AsNoTracking().Where(a => a.DoctorUserId == doctorId &&
            a.StartUtc >= start && a.StartUtc < end && a.Id != excludeId &&
            a.State != AppointmentState.Cancelled && a.State != AppointmentState.NoShow &&
            a.State != AppointmentState.Completed)
            .OrderBy(a => a.StartUtc).Select(a => new { a.Id, a.DoctorUserId,
                a.StartUtc, a.DurationMinutes, a.State }).ToListAsync(ct);
        return rows.Select(r => new ExistingSlot(r.Id, r.DoctorUserId,
            new AppointmentSlot(r.StartUtc, r.DurationMinutes), r.State)).ToArray();
    }

    public async Task<(int WorkStartMinute, int WorkEndMinute, int WorkDaysMask)?> GetDoctorWorkingHoursAsync(
        string doctorId, CancellationToken ct)
    {
        var row = await db.Set<StaffMembership>().AsNoTracking()
            .Where(m => m.UserId == doctorId && m.Role == StaffRole.Doctor &&
                m.Status == MembershipStatus.Active)
            .Select(m => new { m.WorkStartMinute, m.WorkEndMinute, m.WorkDaysMask })
            .FirstOrDefaultAsync(ct);
        return row is null ? null : (row.WorkStartMinute, row.WorkEndMinute, row.WorkDaysMask);
    }

    public async Task<IReadOnlyList<BookingPatientReadModel>> GetPatientsAsync(
        string search, string speciesKey, int limit, string? patientId, string? appointmentId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var normalized = Animora.Application.Text.PersianText.Normalize(search);
        // Empty/short input must not enumerate the clinic's records, even outside the UI.
        if (patientId is null && appointmentId is null && normalized.Length < 2) return [];
        var source = from pet in db.Set<Pet>().AsNoTracking()
            join owner in db.Set<Owner>().AsNoTracking() on pet.OwnerId equals owner.Id
            select new { Pet = pet, Owner = owner };
        if (patientId is not null)
            source = source.Where(r => r.Pet.Id == patientId);
        else if (appointmentId is not null)
            source = source.Where(r => db.Set<Appointment>().Any(a =>
                a.Id == appointmentId && a.PetId == r.Pet.Id));
        else
        {
            // Stored keys already fold Persian/Arabic letters and digits. All filtering
            // stays in SQL; no 500-row preload, client-side scan, or total-count query.
            foreach (var term in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                source = source.Where(r => r.Pet.SearchKey.Contains(term) ||
                    r.Owner.SearchKey.Contains(term) || r.Pet.Id.Contains(term));
            source = speciesKey switch
            {
                "dog" => source.Where(r => r.Pet.Species == PetSpecies.Dog),
                "cat" => source.Where(r => r.Pet.Species == PetSpecies.Cat),
                "bird" => source.Where(r => r.Pet.Species == PetSpecies.Bird),
                "other" => source.Where(r => r.Pet.Species != PetSpecies.Dog &&
                    r.Pet.Species != PetSpecies.Cat && r.Pet.Species != PetSpecies.Bird),
                _ => source
            };
        }
        var rows = await source.OrderBy(r => r.Pet.Name).ThenBy(r => r.Pet.Id)
            .Select(r => new { r.Pet.Id, r.Pet.OwnerId, r.Pet.Name, r.Pet.Species,
                OwnerName = r.Owner.FullName, Phone = r.Owner.PhonePrimary })
            .Take(patientId is not null || appointmentId is not null ? 1 : Math.Clamp(limit, 1, 21))
            .ToListAsync(ct);
        return rows.Select(r => new BookingPatientReadModel(r.Id, r.OwnerId, r.Name,
            r.Species.ToString(), r.OwnerName, r.Phone)).ToArray();
    }

    public async Task<IReadOnlyList<DoctorRosterReadModel>> GetDoctorsAsync(CancellationToken ct) =>
        await (from membership in db.Set<StaffMembership>().AsNoTracking()
            where membership.Role == StaffRole.Doctor && membership.Status == MembershipStatus.Active
            join doctor in db.Set<User>().AsNoTracking() on membership.UserId equals doctor.Id
            orderby doctor.DisplayName
            select new DoctorRosterReadModel(doctor.Id, doctor.DisplayName, ""))
            .ToListAsync(ct);
}
