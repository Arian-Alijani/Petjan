using Animora.Application.UseCases.Dashboard;
using Animora.Contracts.Navigation;
using Animora.Domain.Sales;
using Animora.Domain.Scheduling;
using Animora.Domain.Clinical;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Domain.Patients;
using Animora.Domain.Identity;
using Animora.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>Role-specific sequential reads on one context; at most eight including the version probe.</summary>
public sealed class DashboardQueries(AnimoraLocalDbContext db) : IDashboardQueries
{
    public async Task<string> VersionAsync(string tenantId, CancellationToken ct) =>
        await db.Set<ActivityEvent>().AsNoTracking().Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.Id).Select(x => x.Id).FirstOrDefaultAsync(ct) ?? string.Empty;

    public async Task<DashboardSlice> GetSliceAsync(string tenantId, DashboardDayWindow window,
        string userId, DashboardAccess access, CancellationToken ct)
    {
        var role = access.Role;
        var today = window.LocalDay;
        var date = new DateOnly(today / 10000, today / 100 % 100, today % 100).AddDays(-13);
        IReadOnlyList<DashboardDay> facts = [];
        if (access.Finance)
        {
            var start = date.Year * 10000 + date.Month * 100 + date.Day;
            var stored = await db.Set<DailyFinanceFact>().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.LocalDay >= start && x.LocalDay <= today)
                .Select(x => new DashboardDay(x.LocalDay, x.RevenueMinor)).ToListAsync(ct);
            var byDay = stored.ToDictionary(x => x.LocalDay, x => x.RevenueMinor);
            var series = new DashboardDay[14];
            for (var i = 0; i < series.Length; i++)
            {
                var day = date.AddDays(i);
                var key = day.Year * 10000 + day.Month * 100 + day.Day;
                series[i] = new DashboardDay(key, byDay.GetValueOrDefault(key));
            }
            facts = series;
        }

        var feed = db.Set<ActivityEvent>().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.LocalDay == today);
        feed = feed.Where(x => (access.Visits && x.Kind == "Visit")
            || (access.Vaccination && x.Kind == "Vaccine") || (access.Appointments && x.Kind == "Appointment")
            || (access.Patients && x.Kind == "Patient") || (access.Inventory && x.Kind == "Inventory")
            || (access.Sales && x.Kind == "Sale" && x.Title != "history.payment.registered")
            || (access.Collections && x.Title == "history.payment.registered")
            || (access.Cheques && x.Kind == "Cheque") || (access.Finance && x.Kind == "Expense"));
        if (role == UserRole.Doctor) feed = feed.Where(x => x.ActorUserId == userId);
        // KPIs must describe the whole day, not just the 80-row preview. One aggregate SQL statement.
        var totals = await feed.GroupBy(x => 1).Select(g => new DashboardActivityTotals(
            0L, // Receipts come from the signed Payment ledger, never activity magnitudes.
            g.Count(x => x.Title == "history.visit.completed"),
            g.Count(x => x.Title == "history.vaccine.administered"),
            g.Count(x => x.Kind == "Cheque"))).SingleOrDefaultAsync(ct)
            ?? new DashboardActivityTotals(0, 0, 0, 0);
        DashboardReceipts? receipts = null;
        if (access.Collections)
        {
            receipts = await db.Set<Payment>().AsNoTracking()
                .Where(x => x.TenantId == tenantId).NonCreditSettlements(window.StartUtc, window.EndUtc)
                .GroupBy(x => 1).Select(g => new DashboardReceipts(
                    g.Sum(x => x.Amount.Minor > 0 ? x.Amount.Minor : 0L),
                    g.Sum(x => x.Amount.Minor < 0 ? -x.Amount.Minor : 0L), CurrencyScale.Irt))
                .SingleOrDefaultAsync(ct) ?? new DashboardReceipts(0, 0, CurrencyScale.Irt);
            totals = totals with { Payments = receipts.Net };
        }
        var activity = await feed.OrderByDescending(x => x.Id).Take(80)
            .Select(x => new DashboardActivity(x.Kind, x.Title, x.EntityId,
                access.Finance || access.Collections ? x.AmountMinor : 0L,
                access.Finance || access.Collections ? x.AmountSign : 0, x.ActorUserId, x.OccurredUtc, x.ActorName)).ToListAsync(ct);

        // Separate guarded statements: a forbidden source is not part of SQL at all.
        var openInvoices = db.Set<Invoice>().AsNoTracking().Where(i => i.TenantId == tenantId).OpenInvoices();
        var open = access.Sales ? await openInvoices.CountAsync(ct) : 0;
        IReadOnlyList<DashboardOperationalRow> invoiceRows = [];
        IReadOnlyList<DashboardOperationalRow> readyRows = [];
        if (access.Sales && role == UserRole.Cashier)
        {
            invoiceRows = await (from invoice in openInvoices
                join owner in db.Set<Owner>().AsNoTracking() on invoice.OwnerId equals owner.Id into owners
                from owner in owners.DefaultIfEmpty()
                orderby invoice.IssuedUtc descending, invoice.Id descending
                select new DashboardOperationalRow("Invoice", invoice.InvoiceNumber,
                    owner == null ? invoice.GuestName ?? "مشتری مهمان" : owner.FullName,
                    invoice.IssuedUtc, invoice.State.ToString(), invoice.InvoiceNumber)).Take(8).ToListAsync(ct);
            readyRows = await ReadyBillingProjection.Query(db).OrderByDescending(h => h.CompletedUtc).ThenBy(h => h.Id)
                .Take(8).Select(h => new DashboardOperationalRow(h.Source, h.PetName, h.OwnerName,
                    h.CompletedUtc, "Ready")).ToListAsync(ct);
        }
        var low = access.Inventory ? await db.Set<Animora.Domain.Inventory.Product>().AsNoTracking()
            .Where(i => i.TenantId == tenantId).LowStockProducts(db).CountAsync(ct) : 0;
        IReadOnlyDictionary<string, int> species = new Dictionary<string, int>();
        if (access.Patients && role != UserRole.Doctor)
        {
            var groups = await db.Set<Pet>().AsNoTracking().Where(x => x.Status == PetStatus.Active)
                .GroupBy(x => x.Species).Select(g => new { Species = g.Key, Count = g.Count() }).ToListAsync(ct);
            species = groups.ToDictionary(x => x.Species.ToString(), x => x.Count);
        }

        var appointments = db.Set<Appointment>().AsNoTracking()
            .Where(x => access.Appointments && x.StartUtc >= window.StartUtc && x.StartUtc < window.EndUtc);
        if (role == UserRole.Doctor) appointments = appointments.Where(x => x.DoctorUserId == userId);
        // Aggregate all appointments, but keep the displayed preview bounded.
        var appointmentCounts = await appointments
            .Where(x => x.State != AppointmentState.Cancelled && x.State != AppointmentState.NoShow)
            .GroupBy(x => x.DoctorUserId)
            .Select(g => new { DoctorId = g.Key, Total = g.Count(),
                Name = db.Set<User>().Where(u => u.Id == g.Key).Select(u => u.DisplayName).FirstOrDefault(),
                Done = g.Count(x => x.State == AppointmentState.Completed),
                Queued = g.Count(x => x.State == AppointmentState.Scheduled || x.State == AppointmentState.Confirmed
                    || x.State == AppointmentState.CheckedIn) }).ToListAsync(ct);
        var preview = await (from appointment in appointments.Where(a => a.State != AppointmentState.Cancelled
                && a.State != AppointmentState.NoShow && a.State != AppointmentState.Completed)
            join pet in db.Set<Pet>().AsNoTracking() on appointment.PetId equals pet.Id
            join owner in db.Set<Owner>().AsNoTracking() on appointment.OwnerId equals owner.Id
            join doctor in db.Set<User>().AsNoTracking() on appointment.DoctorUserId equals doctor.Id into doctors
            from doctor in doctors.DefaultIfEmpty()
            orderby appointment.StartUtc, appointment.Id
            select new DashboardAppointment(pet.Name, owner.FullName,
                doctor == null ? "پزشک تعیین نشده" : doctor.DisplayName,
                appointment.StartUtc, appointment.State.ToString())).Take(8).ToListAsync(ct);
        IReadOnlyList<DashboardWorkload> loads = access.Workloads
            ? appointmentCounts.Where(x => x.DoctorId != null).OrderBy(x => x.Name)
                .Select(x => new DashboardWorkload(x.DoctorId!, x.Total, x.Name ?? "پزشک", x.Done)).ToArray()
            : [];
        IReadOnlyList<DashboardOperationalRow> reminders = [];
        if (access.Vaccination && role == UserRole.Doctor)
        {
            // Responsibility for a future dose comes from assigned work, never the actor of the prior dose.
            var assignedPets = appointments.Where(a => a.State != AppointmentState.Cancelled
                && a.State != AppointmentState.NoShow).Select(a => a.PetId);
            reminders = await (from vaccine in db.Set<VaccinationRecord>().AsNoTracking()
                join pet in db.Set<Pet>().AsNoTracking() on vaccine.PetId equals pet.Id
                join owner in db.Set<Owner>().AsNoTracking() on pet.OwnerId equals owner.Id
                where vaccine.State != VaccinationState.Skipped && vaccine.NextDueUtc != null
                    && vaccine.NextDueUtc < window.EndUtc && assignedPets.Contains(vaccine.PetId)
                    && !db.Set<MedicalHistoryEntry>().Any(h => h.Kind == MedicalHistoryKind.Vaccination
                        && h.Title == "Dose administered" && h.SourceEntityId == vaccine.Id)
                orderby vaccine.NextDueUtc, vaccine.Id
                select new DashboardOperationalRow("Vaccine", pet.Name, owner.FullName,
                    vaccine.NextDueUtc!.Value, "Due", vaccine.VaccineName)).Take(8).ToListAsync(ct);
        }
        return new DashboardSlice(facts, activity, loads, species, open, low,
            appointmentCounts.Sum(x => x.Queued), totals, preview, access, receipts, invoiceRows, readyRows, reminders);
    }
}
