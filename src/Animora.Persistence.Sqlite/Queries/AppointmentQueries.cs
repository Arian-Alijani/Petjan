using Animora.Application.Ports;
using Animora.Application.UseCases.Scheduling.ReadModels;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Sales;
using Animora.Domain.Scheduling;
using Animora.Persistence.Sqlite.Context;
using System.Linq.Expressions;
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

    private const int MaxRangeRows = 10_000;
    private const int MaxSearchLength = 64;
    private const int MaxSearchTerms = 4;

    private static DateTimeOffset Utc(long ticks) => new(ticks, TimeSpan.Zero);

    public async Task<IReadOnlyList<AppointmentEventReadModel>> GetRangeAsync(
        long startTicks, long endTicks, string? doctorId, CancellationToken ct)
    {
        var start = Utc(startTicks); var end = Utc(endTicks);
        var source = db.Set<Appointment>().AsNoTracking()
            .Where(a => a.StartUtc >= start && a.StartUtc < end);
        if (!string.IsNullOrEmpty(doctorId)) source = source.Where(a => a.DoctorUserId == doctorId);
        // A22: a 63-day guard bounds the window; this row ceiling is a defensive cap against a corrupt/imported
        // clinic so one read can never realize an unbounded set.
        return (await Events(source).OrderBy(r => r.StartUtc).ThenBy(r => r.Id).Take(MaxRangeRows).ToListAsync(ct))
            .Select(Map).ToArray();
    }

    public async Task<IReadOnlyDictionary<long, int>> GetDayCountsRawAsync(long startTicks, long endTicks, CancellationToken ct)
    {
        var start = Utc(startTicks); var end = Utc(endTicks);
        // A20: one indexed GROUP BY/COUNT statement over the UTC instant. The database returns one bucket per distinct
        // start instant (not one row per appointment); the handler folds those buckets into clinic-local days. The
        // indexed StartUtc predicate is never wrapped in a date function, so the range scan stays index-driven.
        var buckets = await db.Set<Appointment>().AsNoTracking()
            .Where(a => a.StartUtc >= start && a.StartUtc < end && a.State != AppointmentState.Cancelled)
            .GroupBy(a => a.StartUtc)
            .Select(g => new { Start = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return buckets.ToDictionary(b => b.Start.UtcTicks, b => b.Count);
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

    public async Task<AppointmentHistoryPageReadModel> GetHistoryPageAsync(
        AppointmentHistoryCriteria c, CancellationToken ct)
    {
        var to = Utc(c.ToTicks);
        // Terminal states only; every bound is a plain UTC comparison so the (state, start) index stays usable.
        var source = db.Set<Appointment>().AsNoTracking().Where(a => a.StartUtc < to &&
            (a.State == AppointmentState.Completed || a.State == AppointmentState.NoShow ||
             a.State == AppointmentState.Cancelled));
        if (c.FromTicks is { } fromTicks) { var from = Utc(fromTicks); source = source.Where(a => a.StartUtc >= from); }
        if (c.State is { } state) source = source.Where(a => a.State == state);
        if (c.DoctorId is { } doctorId) source = source.Where(a => a.DoctorUserId == doctorId);
        if (c.TypeCode is { } typeCode) source = source.Where(a => a.AppointmentTypeCode == typeCode);
        foreach (var term in c.SearchTerms)
            source = source.Where(a =>
                db.Set<Pet>().Any(p => p.Id == a.PetId && p.SearchKey.Contains(term)) ||
                db.Set<Owner>().Any(o => o.Id == a.OwnerId && o.SearchKey.Contains(term)));
        source = c.Invoice switch
        {
            HistoryInvoiceFilter.Paid => source.Where(InvoiceIs(InvoiceState.Paid)),
            HistoryInvoiceFilter.Partial => source.Where(InvoiceIs(InvoiceState.PartiallyPaid)),
            HistoryInvoiceFilter.Unpaid => source.Where(InvoiceIs(InvoiceState.Posted)),
            HistoryInvoiceFilter.None => source.Where(a => a.VisitId == null || !db.Set<Invoice>().Any(i => i.VisitId == a.VisitId &&
                (i.State == InvoiceState.Posted || i.State == InvoiceState.PartiallyPaid || i.State == InvoiceState.Paid))),
            _ => source,
        };

        // Aggregates cover the whole filtered scope, independent of the page that is returned below.
        var perState = await source.GroupBy(a => a.State)
            .Select(g => new { State = g.Key, Count = g.Count() }).ToListAsync(ct);
        int Of(AppointmentState s) => perState.Where(x => x.State == s).Sum(x => x.Count);
        var events = Events(source);
        var paid = await events.SumAsync(r => r.InvoicePaidMinor ?? 0, ct);
        var owed = await events.SumAsync(r =>
            r.InvoiceState == InvoiceState.Posted || r.InvoiceState == InvoiceState.PartiallyPaid
                ? (r.InvoiceTotalMinor ?? 0) - (r.InvoicePaidMinor ?? 0) : 0, ct);
        var summary = new AppointmentHistorySummary(perState.Sum(x => x.Count), Of(AppointmentState.Completed),
            Of(AppointmentState.Cancelled), Of(AppointmentState.NoShow), paid, owed);

        // One extra row tells the caller whether another page exists without a second count.
        var rows = await events.OrderByDescending(r => r.StartUtc).ThenBy(r => r.Id)
            .Skip(c.Skip).Take(c.Take + 1).ToListAsync(ct);
        return new AppointmentHistoryPageReadModel(rows.Take(c.Take).Select(Map).ToArray(), summary, rows.Count > c.Take);
    }

    // Real FK only (Appointment.VisitId -> Invoice.VisitId); voided invoices never count.
    private Expression<Func<Appointment, bool>> InvoiceIs(InvoiceState state) => a => a.VisitId != null &&
        db.Set<Invoice>().Any(i => i.VisitId == a.VisitId && i.State == state);

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
        // A27: bound the work a broad input can cause. Substring match (names, phone fragments, ids) needs
        // LIKE '%term%', so the workload budget is: <= MaxSearchLength chars, <= MaxSearchTerms terms, 21 rows out.
        // Stable-id lookups (patientId / appointmentId) bypass this and stay direct and uncapped.
        if (normalized.Length > MaxSearchLength) normalized = normalized[..MaxSearchLength];
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
            foreach (var term in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(MaxSearchTerms))
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
