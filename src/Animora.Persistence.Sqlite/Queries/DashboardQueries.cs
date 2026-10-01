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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>Role-specific sequential reads on one context; bounded, including the version probe.</summary>
public sealed class DashboardQueries(AnimoraLocalDbContext db) : IDashboardQueries
{
    /// <summary>
    /// The tenant's source generation: a single primary-key lookup of the trigger-maintained
    /// <see cref="DashboardGeneration"/> row. It advances for every committed write to any table the dashboard
    /// reads — including facts written later by the projection worker — so it never depends on a new feed row.
    /// </summary>
    public async Task<string> VersionAsync(string tenantId, CancellationToken ct) =>
        (await ReadGenerationAsync(tenantId, ct)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private async Task<long> ReadGenerationAsync(string tenantId, CancellationToken ct) =>
        await db.Set<DashboardGeneration>().AsNoTracking().Where(x => x.TenantId == tenantId)
            .Select(x => (long?)x.Value).FirstOrDefaultAsync(ct) ?? 0L;

    /// <summary>
    /// One consistent snapshot: every read below (and the generation, read first) runs inside a single short read
    /// transaction, so a writer committing between two statements can never produce a mix of two states. SQLite
    /// (WAL) fixes the snapshot at the first read and holds no write lock; the transaction is read-only and is
    /// released — never committed — when the reads finish, fail or are cancelled. An ambient transaction (caller
    /// already owns one) is reused, never nested. Derived facts may still lag the source; that lag is reported
    /// in <see cref="DashboardSlice.Projection"/>, not hidden by the transaction.
    /// </summary>
    public async Task<DashboardSlice> GetSliceAsync(string tenantId, DashboardDayWindow window,
        string userId, DashboardAccess access, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
            return await ReadConsistentAsync(tenantId, window, userId, access, ct);

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            // DEFERRED, not EF's default BEGIN IMMEDIATE: a reader must never take the write lock and block the
            // projection worker or a command. The snapshot starts at the first read (the generation).
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            await using var read = await db.Database.UseTransactionAsync(connection.BeginTransaction(deferred: true), ct);
            return await ReadConsistentAsync(tenantId, window, userId, access, ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task<DashboardSlice> ReadConsistentAsync(string tenantId, DashboardDayWindow window,
        string userId, DashboardAccess access, CancellationToken ct)
    {
        var generation = await ReadGenerationAsync(tenantId, ct);
        return (await ReadSliceAsync(tenantId, window, userId, access, ct)) with { Generation = generation };
    }

    private async Task<DashboardSlice> ReadSliceAsync(string tenantId, DashboardDayWindow window,
        string userId, DashboardAccess access, CancellationToken ct)
    {
        var role = access.Role;
        var today = window.LocalDay;
        DashboardProjectionLag? projection = null;
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
            // The facts above are written by the projection worker after the source commit: report how far behind
            // they are instead of presenting them as current. Index IX_OutboxMessages_Status_NextAttemptUtc.
            var lag = await db.Set<OutboxMessage>().AsNoTracking().Where(x => x.Status != OutboxStatus.Succeeded)
                .GroupBy(x => 1).Select(g => new
                {
                    Pending = g.Count(x => x.Status != OutboxStatus.DeadLettered),
                    Failed = g.Count(x => x.Status == OutboxStatus.DeadLettered),
                }).SingleOrDefaultAsync(ct);
            projection = new DashboardProjectionLag(lag?.Pending ?? 0, lag?.Failed ?? 0);
        }

        // "Mine" is the doctor responsible for the clinical entity, never the actor of the command that
        // wrote an audit/activity row (a receptionist may book/complete work for doctor-A).
        // The audit feed therefore contributes only to non-doctor widgets.
        DashboardActivityTotals totals;
        IReadOnlyList<DashboardActivity> activity = [];
        DashboardReceipts? receipts = null;
        if (role == UserRole.Doctor)
        {
            var visitCount = access.Visits ? await db.Set<Visit>().AsNoTracking()
                .Where(v => v.TenantId == tenantId && v.DoctorUserId == userId && v.State != VisitState.Cancelled
                    && v.CompletedUtc != null && v.CompletedUtc >= window.StartUtc && v.CompletedUtc < window.EndUtc)
                .CountAsync(ct) : 0;
            // A dose belongs to the doctor who administered it or whose visit it was given in.
            var vaccineCount = access.Vaccination ? await db.Set<VaccinationRecord>().AsNoTracking()
                .Where(v => v.TenantId == tenantId && v.AdministeredUtc >= window.StartUtc && v.AdministeredUtc < window.EndUtc
                    && (v.AdministeredByUserId == userId || (v.VisitId != null
                        && db.Set<Visit>().Any(x => x.Id == v.VisitId && x.DoctorUserId == userId))))
                .CountAsync(ct) : 0;
            totals = new DashboardActivityTotals(0, visitCount, vaccineCount, 0);
        }
        else
        {
            var feed = db.Set<ActivityEvent>().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.LocalDay == today);
            feed = feed.Where(x => (access.Visits && x.Kind == "Visit")
                || (access.Vaccination && x.Kind == "Vaccine") || (access.Appointments && x.Kind == "Appointment")
                || (access.Patients && x.Kind == "Patient") || (access.Inventory && x.Kind == "Inventory")
                || (access.Sales && x.Kind == "Sale" && x.Title != "history.payment.registered")
                || (access.Collections && x.Title == "history.payment.registered")
                || (access.Cheques && x.Kind == "Cheque") || (access.Finance && x.Kind == "Expense"));
            // KPIs must describe the whole day, not just the 80-row preview. One aggregate SQL statement.
            totals = await feed.GroupBy(x => 1).Select(g => new DashboardActivityTotals(
                0L, // Receipts come from the signed Payment ledger, never activity magnitudes.
                g.Count(x => x.Title == "history.visit.completed"),
                g.Count(x => x.Title == "history.vaccine.administered"),
                g.Count(x => x.Kind == "Cheque"))).SingleOrDefaultAsync(ct)
                ?? new DashboardActivityTotals(0, 0, 0, 0);
            activity = await feed.OrderByDescending(x => x.Id).Take(80)
                .Select(x => new DashboardActivity(x.Kind, x.Title, x.EntityId,
                    access.Finance || access.Collections ? x.AmountMinor : 0L,
                    access.Finance || access.Collections ? x.AmountSign : 0, x.ActorUserId, x.OccurredUtc, x.ActorName, x.Status)).ToListAsync(ct);
        }
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

        // Separate guarded statements: a forbidden source is not part of SQL at all.
        var openInvoices = db.Set<Invoice>().AsNoTracking().Where(i => i.TenantId == tenantId).OpenInvoices();
        // One aggregate: open (Draft/Posted/PartiallyPaid, same as the cashier page) and the payable subset.
        // Payment queue = issued invoices that still need money; drafts are open but not yet payable.
        var invoiceCounts = access.Sales ? await openInvoices.GroupBy(i => 1).Select(g => new
        {
            Open = g.Count(),
            Awaiting = g.Count(i => i.State == InvoiceState.Posted || i.State == InvoiceState.PartiallyPaid),
        }).SingleOrDefaultAsync(ct) : null;
        var open = invoiceCounts?.Open ?? 0;
        var awaitingPayment = invoiceCounts?.Awaiting ?? 0;
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
                Done = g.Count(x => x.State == AppointmentState.Completed) }).ToListAsync(ct);
        // Active queue = the reception page's derived queue (appointment LEFT JOIN entry UNION walk-ins),
        // restricted to non-terminal states. Doctor scope uses the assigned doctor, never the actor.
        var queueCount = 0;
        if (access.Appointments)
        {
            queueCount = await (from a in appointments.Where(a => a.State != AppointmentState.Cancelled
                    && a.State != AppointmentState.NoShow)
                join e in db.Set<ReceptionQueueEntry>().AsNoTracking() on a.Id equals e.AppointmentId into entries
                from e in entries.DefaultIfEmpty()
                where (e == null && (a.State == AppointmentState.Scheduled || a.State == AppointmentState.Confirmed
                        || a.State == AppointmentState.CheckedIn || a.State == AppointmentState.InVisit))
                    || (e != null && (e.State == QueueState.Waiting || e.State == QueueState.Called || e.State == QueueState.InVisit))
                select a.Id).CountAsync(ct);
            var walkIns = db.Set<ReceptionQueueEntry>().AsNoTracking().Where(r => r.AppointmentId == null
                && r.EnqueuedUtc >= window.StartUtc && r.EnqueuedUtc < window.EndUtc
                && (r.State == QueueState.Waiting || r.State == QueueState.Called || r.State == QueueState.InVisit));
            if (role == UserRole.Doctor) walkIns = walkIns.Where(r => r.DoctorUserId == userId);
            queueCount += await walkIns.CountAsync(ct);
        }
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
            ? appointmentCounts.Where(x => x.DoctorId != null)
                .OrderBy(x => x.Name ?? "\uFFFF", StringComparer.Ordinal).ThenBy(x => x.DoctorId, StringComparer.Ordinal)
                .Select(x => new DashboardWorkload(x.DoctorId!, x.Total, x.Name ?? string.Empty, x.Done)).ToArray()
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
            queueCount, totals, preview, access, receipts, invoiceRows, readyRows, reminders, awaitingPayment, projection);
    }
}
