using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Animora.Client.Services.Dashboard;
using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Dashboard;
using Animora.Domain.Patients;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using Animora.Domain.Events;
using Animora.Application.Activity;
using Animora.Application.Outbox;
using Animora.Persistence.Sqlite.Maintenance;
using Animora.Persistence.Sqlite.Activity;
using Animora.Domain.Scheduling;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Contracts.Navigation;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>
/// Persistence-only dashboard budget (not the production authorization pipeline): explicit test
/// capabilities stay within a role-specific <c>DbCommand</c> bound, and the Manager's "today
/// revenue" equals the finance projection's same-day figure (single source of truth —
/// <c>DailyFinanceFact</c>), and the Doctor snapshot exposes no manager-only rich widget.
/// </summary>
public sealed class DashboardBudgetTests
{
    // The persistence fixture and CommandCounter live in Animora.Persistence.Tests (referenced here).
    private const int Today = 20260301;
    private static DashboardDayWindow Window => DashboardDayWindow.For(new DateOnly(2026, 3, 1), TimeZoneInfo.Utc);

    private static async Task SeedRevenueFactAsync(Animora.Persistence.Tests.LocalStoreTestFixture fixture, int localDay, long revenue)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var fact = DailyFinanceFact.Create(fixture.TenantId, localDay);
        fact.Apply(revenue: revenue);
        db.Add(fact);
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(UserRole.Manager, 10)]
    [InlineData(UserRole.Doctor, 6)]
    [InlineData(UserRole.Cashier, 10)]
    [InlineData(UserRole.Receptionist, 10)]
    public async Task Snapshot_StaysWithinRoleCommandBudget(UserRole role, int expectedCommands)
    {
        using var fixture = new Animora.Persistence.Tests.LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedRevenueFactAsync(fixture, Today, 4_200_000);

        var (counting, counter) = fixture.CreateCountingContext();
        await using (counting)
        {
            var queries = new DashboardQueries(counting);
            counter.Reset();
            // Count persistence reads only; D24 separately measures live authorization + service scopes.
            _ = await queries.VersionAsync(fixture.TenantId, default);
            _ = await queries.GetSliceAsync(fixture.TenantId, Window, "user-1", TestAccess(role), default);
            Assert.Equal(expectedCommands, counter.Count);
        }
    }

    [Fact]
    public async Task OperationalLists_ReadCurrentState_NotRecentEvents()
    {
        using var fixture = new Animora.Persistence.Tests.LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var emptyQueries = new DashboardQueries(db);
        var empty = await emptyQueries.GetSliceAsync(fixture.TenantId, Window, "cashier", TestAccess(UserRole.Cashier), default);
        Assert.Empty(empty.OpenInvoiceRows!);
        Assert.Empty(empty.ReadyBillingRows!);
        var emptyDoctor = await emptyQueries.GetSliceAsync(fixture.TenantId, Window, "doc-A", TestAccess(UserRole.Doctor), default);
        Assert.Empty(emptyDoctor.Appointments!);
        Assert.Empty(emptyDoctor.ReminderRows!);
        var owner = Owner.Register(fixture.TenantId, "مالک", "09120000001", fixture.Clock);
        var pet = Pet.Register(fixture.TenantId, owner.Id, "بیمار", PetSpecies.Dog, fixture.Clock);
        db.AddRange(owner, pet);
        var appointment = Appointment.Book(fixture.TenantId, pet.Id, owner.Id, "CHECKUP", Window.StartUtc.AddHours(9),
            30, AppointmentSource.Phone, "receptionist", fixture.Clock, "doc-A").Value!;
        var cancelled = Appointment.Book(fixture.TenantId, pet.Id, owner.Id, "CHECKUP", Window.StartUtc.AddHours(10),
            30, AppointmentSource.Phone, "receptionist", fixture.Clock, "doc-A").Value!;
        Assert.True(cancelled.Cancel("لغو", fixture.Clock).IsSuccess);
        var invoice = Invoice.Create(fixture.TenantId, "INV-1", InvoiceKind.DirectSale, Window.StartUtc.AddDays(-1),
            "cashier", ownerId: owner.Id, petId: pet.Id).Value!;
        Assert.True(invoice.AddLine(new InvoiceLine("line", InvoiceLineKind.Service, null, null, null,
            "Consultation", new Quantity(1, "PCS"), Money.Irt(100), 0, 0, InvoiceLineSource.Manual, null)).IsSuccess);
        Assert.True(invoice.Post(fixture.Clock).IsSuccess);
        for (var i = 0; i < 2; i++)
        {
            var payment = Payment.Create(fixture.TenantId, invoice.Id, PaymentMethod.Card, Money.Irt(10),
                Window.StartUtc.AddHours(9), "cashier").Value!;
            Assert.True(invoice.ApplyPayment(payment).IsSuccess);
            db.Add(payment);
        }
        var ready = VisitBillingHandoff.Create(fixture.TenantId, "visit-ready", owner.Id, pet.Id, "doc-A", Window.StartUtc, "[]");
        var billed = VisitBillingHandoff.Create(fixture.TenantId, "visit-billed", owner.Id, pet.Id, "doc-A", Window.StartUtc, "[]");
        billed.MarkInvoiced(invoice.Id);
        var vaccine = Animora.Domain.Clinical.VaccinationRecord.Administer(fixture.TenantId, pet.Id, "DHPP", "واکسن",
            1, Window.StartUtc.AddDays(-21), "doc-B", fixture.Clock).Value!;
        var completedVaccine = Animora.Domain.Clinical.VaccinationRecord.Administer(fixture.TenantId, pet.Id, "RABIES", "انجام شده",
            1, Window.StartUtc.AddDays(-400), "doc-B", fixture.Clock).Value!;
        db.Add(new Animora.Domain.Clinical.MedicalHistoryEntry(fixture.TenantId, pet.Id,
            Animora.Domain.Clinical.MedicalHistoryKind.Vaccination, "Dose administered", Window.StartUtc,
            "receptionist", fixture.Clock, sourceEntityId: completedVaccine.Id));
        db.AddRange(appointment, cancelled, invoice, ready, billed, vaccine, completedVaccine);
        for (var i = 0; i < 90; i++)
            db.Add(ActivityEvent.Create(fixture.TenantId, Window.StartUtc.AddHours(12), Today, "Inventory",
                "history.inventory.changed", "{}", "Inventory", $"stock-{i}", 0, 0, "Done", "receptionist", "پذیرش", null));
        await db.SaveChangesAsync();
        var queries = new DashboardQueries(db);
        var doctor = await queries.GetSliceAsync(fixture.TenantId, Window, "doc-A", TestAccess(UserRole.Doctor), default);
        Assert.Single(doctor.Appointments!);
        Assert.Equal("بیمار", Assert.Single(doctor.ReminderRows!).Name);
        Assert.Empty(doctor.Activity); // Operational rows exist without any matching event.
        var cashier = await queries.GetSliceAsync(fixture.TenantId, Window, "cashier", TestAccess(UserRole.Cashier), default);
        Assert.Equal("INV-1", Assert.Single(cashier.OpenInvoiceRows!).Name);
        Assert.Equal("PartiallyPaid", cashier.OpenInvoiceRows![0].State);
        Assert.Single(cashier.ReadyBillingRows!);
        var reference = await scope.ServiceProvider.GetRequiredService<ISalesQueries>().GetReadyForBillingAsync(default);
        Assert.Single(reference);
        Assert.Equal(reference[0].PetName, cashier.ReadyBillingRows![0].Name);
        var mapped = InvokeMap(UserRole.Cashier, cashier);
        Assert.Single(mapped.PrimaryList);
        Assert.Single(mapped.SecondaryList);
        Assert.Equal("آماده صدور فاکتور", mapped.SecondaryListTitle);
        var denied = await queries.GetSliceAsync(fixture.TenantId, Window, "cashier",
            TestAccess(UserRole.Cashier) with { Sales = false }, default);
        Assert.Empty(denied.OpenInvoiceRows!);
        Assert.Empty(denied.ReadyBillingRows!);
    }

    [Fact]
    public async Task ManagerRevenue_EqualsFinanceSameDayFigure()
    {
        const long expected = 7_654_321;
        using var fixture = new Animora.Persistence.Tests.LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedRevenueFactAsync(fixture, Today, expected);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var slice = await new DashboardQueries(db).GetSliceAsync(fixture.TenantId, Window, "mgr", TestAccess(UserRole.Manager), default);

        // The mapper's "today revenue" stat is the last (today) revenue-day value verbatim.
        var snapshot = InvokeMap(UserRole.Manager, slice);
        var todayRevenueStat = snapshot.Stats[0];
        Assert.Equal(expected.ToString("N0", CultureInfo.InvariantCulture), todayRevenueStat.Value);

        // And it comes straight from DailyFinanceFact — the same source the finance screen reads.
        var factRevenue = await db.Set<DailyFinanceFact>().AsNoTracking()
            .Where(x => x.TenantId == fixture.TenantId && x.LocalDay == Today)
            .Select(x => x.RevenueMinor).SingleAsync();
        Assert.Equal(expected, factRevenue);
        Assert.Equal(factRevenue, slice.RevenueDays[^1].RevenueMinor);
    }

    [Fact]
    public async Task DoctorSnapshot_HasNoManagerWidget()
    {
        using var fixture = new Animora.Persistence.Tests.LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedRevenueFactAsync(fixture, Today, 1_000_000);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var slice = await new DashboardQueries(db).GetSliceAsync(fixture.TenantId, Window, "doc", TestAccess(UserRole.Doctor), default);

        var snapshot = InvokeMap(UserRole.Doctor, slice);
        // All three manager rich widgets must be absent for a Doctor.
        Assert.Null(snapshot.RevenuePulse);
        Assert.Null(snapshot.DoctorLoads);
        Assert.Null(snapshot.Species);
    }

    [Fact]
    public async Task ManagerSnapshot_CarriesAllThreeRichWidgets()
    {
        using var fixture = new Animora.Persistence.Tests.LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedRevenueFactAsync(fixture, Today, 1_000_000);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var slice = await new DashboardQueries(db).GetSliceAsync(fixture.TenantId, Window, "mgr", TestAccess(UserRole.Manager), default);

        var snapshot = InvokeMap(UserRole.Manager, slice);
        Assert.NotNull(snapshot.RevenuePulse);
        Assert.NotNull(snapshot.DoctorLoads);
        Assert.NotNull(snapshot.Species);
    }

    [Theory]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.Doctor)]
    [InlineData(UserRole.Cashier)]
    public async Task DailyKpis_AreNotTruncatedByRecentActivityLimit(UserRole role)
    {
        using var fixture = new Animora.Persistence.Tests.LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        void Add(string kind, string title, int count, long amount = 0, string actor = "user-1", string? tenant = null)
        {
            for (var i = 0; i < count; i++)
                db.Add(ActivityEvent.Create(tenant ?? fixture.TenantId, fixture.Clock.UtcNow, Today,
                    kind, title, "{}", kind, $"entity-{i}", amount, amount > 0 ? 1 : 0, "Done", actor, actor, null));
        }
        Add("Visit", "history.visit.completed", 101);
        Add("Sale", "history.payment.registered", 95, 1000);
        for (var i = 0; i < 95; i++)
            db.Add(Payment.Create(fixture.TenantId, "invoice", PaymentMethod.Card, Money.Irt(1000),
                Window.StartUtc.AddHours(9), "user-1").Value!);
        Add("Vaccine", "history.vaccine.administered", 105);
        Add("Cheque", "history.cheque.changed", 90);
        Add("Visit", "history.visit.completed", 7, actor: "other-doctor");
        Add("Visit", "history.visit.completed", 11, tenant: "other-tenant");
        await db.SaveChangesAsync();

        var slice = await new DashboardQueries(db).GetSliceAsync(fixture.TenantId, Window, "user-1", TestAccess(role), default);
        Assert.Equal(80, slice.Activity.Count); // Preview stays cheap; statistics must not use it.
        Assert.Equal(role == UserRole.Doctor ? 101 : 108, slice.ActivityTotals!.Visits);
        Assert.Equal(role == UserRole.Doctor ? 0 : 95_000, slice.ActivityTotals.Payments);
        Assert.Equal(105, slice.ActivityTotals.Vaccinations);
        Assert.Equal(role == UserRole.Doctor ? 0 : 90, slice.ActivityTotals.Cheques);
        var mapped = InvokeMap(role, slice);
        if (role == UserRole.Manager) Assert.Equal("108", mapped.Stats[1].Value);
        if (role == UserRole.Doctor)
        {
            Assert.Equal("101", mapped.Stats[0].Value);
            Assert.Equal("105", mapped.Stats[3].Value);
            Assert.Empty(slice.RevenueDays);
            Assert.Empty(slice.Workloads);
            Assert.Empty(slice.Species);
        }
        if (role == UserRole.Cashier) Assert.Equal("95,000", mapped.Stats[0].Value);
    }

    [Fact]
    public async Task RevenueSeries_FillsMissingDays_AndSpeciesCountsOnlyActivePets()
    {
        using var fixture = new Animora.Persistence.Tests.LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedRevenueFactAsync(fixture, 20260216, 100);
        await SeedRevenueFactAsync(fixture, 20260228, 300);
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var owner = Owner.Register(fixture.TenantId, "Owner", "09120000001", fixture.Clock);
        db.Add(owner);
        db.Add(Pet.Register(fixture.TenantId, owner.Id, "Dog", PetSpecies.Dog, fixture.Clock));
        var inactive = Pet.Register(fixture.TenantId, owner.Id, "Cat", PetSpecies.Cat, fixture.Clock);
        inactive.SetStatus(PetStatus.Deceased);
        db.Add(inactive);
        await db.SaveChangesAsync();
        var slice = await new DashboardQueries(db).GetSliceAsync(fixture.TenantId, Window, "mgr", TestAccess(UserRole.Manager), default);
        Assert.Equal(14, slice.RevenueDays.Count);
        Assert.Equal(20260216, slice.RevenueDays[0].LocalDay);
        Assert.Equal(100, slice.RevenueDays[0].RevenueMinor);
        Assert.Equal(300, slice.RevenueDays[^2].RevenueMinor);
        Assert.Equal(Today, slice.RevenueDays[^1].LocalDay);
        Assert.Equal(0, slice.RevenueDays[^1].RevenueMinor);
        Assert.Equal(1, Assert.Single(slice.Species).Value);
        Assert.Equal("Dog", Assert.Single(slice.Species).Key);
    }

    [Fact]
    public async Task QueueAndWorkloads_RespectClinicMidnightAndDoctorFilter()
    {
        using var fixture = new Animora.Persistence.Tests.LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var owner = Owner.Register(fixture.TenantId, "Owner", "09120000001", fixture.Clock);
        var pet = Pet.Register(fixture.TenantId, owner.Id, "Pet", PetSpecies.Dog, fixture.Clock);
        db.AddRange(owner, pet);
        var window = DashboardDayWindow.For(new DateOnly(2026, 3, 1), new FixedZone().GetClinicTimeZone());
        foreach (var (at, doctor) in new (DateTimeOffset, string?)[]
        {
            (window.StartUtc.AddTicks(-1), "doc-1"), // Previous clinic day, but UTC date differs.
            (window.StartUtc, "doc-1"),
            (window.EndUtc.AddTicks(-1), "doc-1"),
            (window.EndUtc, "doc-1"), // Next clinic day, despite the same UTC date.
            (window.StartUtc.AddHours(1), "doc-2"),
            (window.StartUtc.AddHours(2), null),
        })
            db.Add(Appointment.Book(fixture.TenantId, pet.Id, owner.Id, "CHECKUP", at, 30,
                AppointmentSource.Phone, "user-1", fixture.Clock, doctor).Value!);
        await db.SaveChangesAsync();
        var queries = new DashboardQueries(db);
        var manager = await queries.GetSliceAsync(fixture.TenantId, window, "mgr", TestAccess(UserRole.Manager), default);
        Assert.Equal(4, manager.QueueCount);
        Assert.Equal(4, manager.Appointments!.Count);
        Assert.All(manager.Appointments, x => Assert.Equal("Pet", x.PetName));
        Assert.Equal(2, manager.Workloads.Count);
        Assert.Equal(2, manager.Workloads.Single(x => x.DoctorId == "doc-1").Total);
        var doctorSlice = await queries.GetSliceAsync(fixture.TenantId, window, "doc-1", TestAccess(UserRole.Doctor), default);
        Assert.Equal(2, doctorSlice.QueueCount);
        Assert.Empty(doctorSlice.Workloads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Receipts_UseRealSignedPayments_AndRefundLiveRebuildAgree(bool nextDayRefund)
    {
        using var fixture = new Animora.Persistence.Tests.LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var window = DashboardDayWindow.For(new DateOnly(2026, 3, 1), new FixedZone().GetClinicTimeZone());
        var at = window.StartUtc.AddHours(9);
        var invoice = Invoice.Create(fixture.TenantId, "REFUND-1", InvoiceKind.DirectSale, at,
            "cashier", guestName: "Guest").Value!;
        Assert.True(invoice.AddLine(new InvoiceLine("line", InvoiceLineKind.Service, null, null, null,
            "Consultation", new Quantity(1, "PCS"), Money.Irt(100), 0, 0, InvoiceLineSource.Manual, null)).IsSuccess);
        Assert.True(invoice.Post(fixture.Clock).IsSuccess);
        var receipt = Payment.Create(fixture.TenantId, invoice.Id, PaymentMethod.Card, Money.Irt(100), at, "cashier").Value!;
        Assert.True(invoice.ApplyPayment(receipt).IsSuccess);
        var refundAt = nextDayRefund ? window.EndUtc : window.EndUtc.AddTicks(-1);
        var refund = Payment.Create(fixture.TenantId, invoice.Id, PaymentMethod.Card, Money.Irt(-30),
            refundAt, "cashier", reversesPaymentId: receipt.Id).Value!;
        Assert.True(invoice.ApplyRefund(new InvoiceAdjustment("adjustment", AdjustmentKind.Refund,
            Money.Irt(-30), "return", refundAt, "cashier", null, null), refund).IsSuccess);
        db.AddRange(invoice, receipt, refund);
        // Other currencies and the half-open day boundary must not silently contaminate toman totals.
        db.Add(Payment.Create(fixture.TenantId, invoice.Id, PaymentMethod.Card, Money.Of(999, "USD"), at, "cashier").Value!);
        db.Add(Payment.Create(fixture.TenantId, invoice.Id, PaymentMethod.Card, Money.Irt(999), window.EndUtc, "cashier").Value!);
        var writer = scope.ServiceProvider.GetRequiredService<IActivityFeedWriter>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();
        foreach (var source in invoice.DomainEvents)
        {
            if (ActivityFeedMapper.Map(source) is { } draft) await writer.WriteAsync(draft, default);
            Assert.True((await outbox.EnqueueAsync(source.GetType().Name, source)).IsSuccess);
        }
        await db.SaveChangesAsync();
        var liveRefund = await db.Set<ActivityEvent>().AsNoTracking().SingleAsync(x => x.EntityId == refund.Id);
        Assert.Equal(30, liveRefund.AmountMinor);
        Assert.Equal(-1, liveRefund.AmountSign);
        var queries = new DashboardQueries(db);
        var slice = await queries.GetSliceAsync(fixture.TenantId, window, "cashier", TestAccess(UserRole.Cashier), default);
        Assert.Equal(100, slice.Receipts!.Gross);
        Assert.Equal(nextDayRefund ? 0 : 30, slice.Receipts.Refunds);
        Assert.Equal(nextDayRefund ? 100 : 70, slice.ActivityTotals!.Payments);
        Assert.Equal("IRT", slice.Receipts.Currency);
        Assert.Contains("تومان", InvokeMap(UserRole.Cashier, slice).Stats[0].Label);
        var rebuilt = await scope.ServiceProvider.GetRequiredService<ILocalStoreMaintenance>().RebuildActivityFeedAsync(default);
        Assert.True(rebuilt.IsSuccess, rebuilt.Detail);
        var rebuiltRefund = await db.Set<ActivityEvent>().AsNoTracking().SingleAsync(x => x.EntityId == refund.Id);
        Assert.Equal(liveRefund.AmountMinor, rebuiltRefund.AmountMinor);
        Assert.Equal(liveRefund.AmountSign, rebuiltRefund.AmountSign);
    }

    [Theory]
    [InlineData(PaymentMethod.Cash, 70)]
    [InlineData(PaymentMethod.Card, 70)]
    [InlineData(PaymentMethod.Transfer, 70)]
    [InlineData(PaymentMethod.Cheque, 70)]
    [InlineData(PaymentMethod.Wallet, 70)]
    [InlineData(PaymentMethod.Insurance, 70)]
    [InlineData(PaymentMethod.Credit, 0)]
    [InlineData((PaymentMethod)999, 0)]
    public async Task NonCreditSettlements_DashboardAndCashierShareSignedCurrencyRule(PaymentMethod method, long expected)
    {
        using var fixture = new Animora.Persistence.Tests.LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        foreach (var amount in new[] { 100L, -30L })
            db.Add(Payment.Create(fixture.TenantId, "invoice", method, Money.Irt(amount),
                Window.StartUtc.AddHours(9), "cashier", cashSessionId: "session", chequeId: "cheque").Value!);
        await db.SaveChangesAsync();
        var slice = await new DashboardQueries(db).GetSliceAsync(fixture.TenantId, Window, "cashier", TestAccess(UserRole.Cashier), default);
        var cashier = await scope.ServiceProvider.GetRequiredService<ICashierQueries>().GetStatsAsync(Window.StartUtc, Window.EndUtc, default);
        Assert.Equal(expected, slice.ActivityTotals!.Payments);
        Assert.Equal(expected, cashier.CollectedMinor);
        Assert.Equal(expected == 0 ? 0 : 100, slice.Receipts!.Gross);
        Assert.Equal(expected == 0 ? 0 : 30, slice.Receipts.Refunds);
        Assert.Contains("غیرنسیه", InvokeMap(UserRole.Cashier, slice).Stats[0].Label);
    }

    [Fact]
    public async Task LowStock_CountsActiveProductsOnce_AtVariantReorderThreshold()
    {
        using var fixture = new Animora.Persistence.Tests.LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        void Add(string sku, long? stock, decimal reorder, bool active = true, bool activeVariant = true, bool secondLow = false)
        {
            var variants = new[]
            {
                new Animora.Domain.Inventory.ProductVariant(sku + "-v", sku + "-v", "DEFAULT", "", Money.Irt(1), Money.Irt(2),
                    new Quantity(reorder, "PCS"), null, activeVariant),
                new Animora.Domain.Inventory.ProductVariant(sku + "-v2", sku + "-v2", "size", "other", Money.Irt(1), Money.Irt(2),
                    new Quantity(secondLow ? 10 : 0, "PCS"), null, true),
            };
            var product = Animora.Domain.Inventory.Product.Create(fixture.TenantId, sku, sku, "category",
                Animora.Domain.Inventory.ProductKind.Medicine, "PCS", Money.Irt(1), Money.Irt(2), variants).Value!;
            if (!active) Assert.True(product.Deactivate().IsSuccess);
            db.Add(product);
            foreach (var variant in variants)
            {
                if (stock is null && variant == variants[0]) continue;
                var snap = StockOnHandSnapshot.Create(fixture.TenantId, product.Id, variant.VariantId);
                snap.Reset(variant == variants[0] ? stock!.Value : 1000, Window.StartUtc, "movement");
                db.Add(snap);
            }
        }
        Add("below", 2000, 10, secondLow: true);
        Add("equal", 10000, 10);
        Add("absent", null, 10);
        Add("inactive", 2000, 10, active: false);
        Add("inactive-variant", 2000, 10, activeVariant: false);
        Add("above", 11000, 10);
        await db.SaveChangesAsync();
        var slice = await new DashboardQueries(db).GetSliceAsync(fixture.TenantId, Window, "manager", TestAccess(UserRole.Manager), default);
        var catalog = await scope.ServiceProvider.GetRequiredService<IInventoryQueries>().GetCatalogAsync(null, null, true, 1, 20, default);
        Assert.Equal(new[] { "absent", "below", "equal" }, catalog.Items.Select(x => x.Sku).OrderBy(x => x).ToArray());
        Assert.Equal(3, slice.LowStockCount);
        Assert.Equal(slice.LowStockCount, catalog.TotalCount);
    }

    [Fact]
    public async Task ChequeKpi_CountsTodayStateChanges_NotDistinctCheques_AndRequiresPermission()
    {
        using var fixture = new Animora.Persistence.Tests.LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        foreach (var day in new[] { Today - 1, Today, Today, Today })
            db.Add(ActivityEvent.Create(fixture.TenantId, Window.StartUtc.AddHours(9), day, "Cheque",
                "history.cheque.changed", "{}", "Cheque", "one-cheque", 0, 0, "Pending", "cashier", "", null));
        await db.SaveChangesAsync();
        var access = TestAccess(UserRole.Cashier);
        var queries = new DashboardQueries(db);
        var slice = await queries.GetSliceAsync(fixture.TenantId, Window, "cashier", access, default);
        Assert.Equal(3, slice.ActivityTotals!.Cheques);
        var stat = Assert.Single(InvokeMap(UserRole.Cashier, slice).Stats, x => x.IconKey == "IconCheque");
        Assert.Equal("تغییر وضعیت چک امروز", stat.Label);
        Assert.Equal("3", stat.Value);
        var denied = await queries.GetSliceAsync(fixture.TenantId, Window, "cashier", access with { Cheques = false }, default);
        Assert.Equal(0, denied.ActivityTotals!.Cheques);
        Assert.DoesNotContain(InvokeMap(UserRole.Cashier, denied).Stats, x => x.IconKey == "IconCheque");
    }

    [Fact]
    public void Snapshot_FreezesInputAndNestedCollections_IncludingRecordInitializers()
    {
        var stat = new Contracts.Dashboard.DashboardStat("IconPaw", "Original", "1", null, true);
        var stats = new[] { stat };
        var rows = new[] { new Contracts.Dashboard.DashboardListItem("", "Original", "", "", Contracts.Dashboard.DashboardTone.Info) };
        var points = new[] { 1d, -0.5d };
        var species = new[] { new Contracts.Dashboard.DashboardSpecies("Dog", 1, "#000000") };
        var loads = new[] { new Contracts.Dashboard.DashboardDoctorLoad("D", "Doctor", "", 1, 2, "#000000") };
        var snapshot = new Contracts.Dashboard.DashboardSnapshot(UserRole.Manager, "", "", stats, "", rows, "", rows,
            new("1", "", points), loads, species);
        stats[0] = stat with { Label = "Mutated" };
        rows[0] = rows[0] with { Title = "Mutated" };
        points[0] = 999;
        species[0] = species[0] with { Count = 999 };
        loads[0] = loads[0] with { Done = 999 };
        Assert.Equal("Original", snapshot.Stats[0].Label);
        Assert.Equal("Original", snapshot.PrimaryList[0].Title);
        Assert.Equal("Original", snapshot.SecondaryList[0].Title);
        Assert.Equal(1, snapshot.RevenuePulse!.Points[0]);
        Assert.Equal(1, snapshot.Species![0].Count);
        Assert.Equal(1, snapshot.DoctorLoads![0].Done);
        Assert.Throws<NotSupportedException>(() => ((IList<Contracts.Dashboard.DashboardStat>)snapshot.Stats)[0] = stat);
        Assert.Throws<NotSupportedException>(() => ((IList<double>)snapshot.RevenuePulse.Points)[0] = 999);
        var changed = snapshot with { Stats = stats };
        stats[0] = stat with { Label = "Changed again" };
        Assert.Equal("Mutated", changed.Stats[0].Label);
        Assert.NotNull(new Contracts.Dashboard.DashboardSnapshot(UserRole.Doctor, "", "", [], "", [], "", []).Stats);
    }

    private sealed class FixedZone : IClinicTimeZoneProvider
    {
        public TimeZoneInfo GetClinicTimeZone() => TimeZoneInfo.CreateCustomTimeZone(
            "test-clinic", TimeSpan.FromMinutes(210), "Test clinic", "Test clinic");
    }

    private static DashboardAccess TestAccess(UserRole role) => new(role,
        role is UserRole.Manager or UserRole.Receptionist, role != UserRole.Doctor,
        role is UserRole.Manager or UserRole.Receptionist, role != UserRole.Doctor,
        true, true, role != UserRole.Doctor, true, true, role is UserRole.Manager or UserRole.Receptionist);

    [Fact]
    public void Mapping_UsesRealAppointmentDetailsAndSemanticEventIcons()
    {
        var slice = new DashboardSlice([], [new("Appointment", "history.appointment.booked", "opaque-id", 0, 0, "", Window.StartUtc, "مدیر")],
            [new("doctor-id", 5, "دکتر رضایی", 2)], new Dictionary<string, int> { ["Cat"] = 3 }, 0, 0, 0,
            Appointments: [new("پیشی", "مالک", "دکتر رضایی", Window.StartUtc.AddHours(9), "Confirmed")]);
        var snapshot = InvokeMap(UserRole.Manager, slice);
        var appointment = Assert.Single(snapshot.PrimaryList);
        Assert.Equal("پیشی", appointment.Title);
        Assert.Contains("09:00", appointment.Subtitle);
        Assert.Contains("مالک", appointment.Subtitle);
        Assert.Equal("تأیید شده", appointment.TagText);
        Assert.Equal("IconCalendar", appointment.IconKey);
        var activity = Assert.Single(snapshot.SecondaryList);
        Assert.Equal("history.appointment.booked", activity.Title); // Localized by Desktop's existing HistoryText.
        Assert.DoesNotContain("opaque-id", activity.Subtitle);
        Assert.Equal("نوبت", activity.TagText);
        Assert.Equal("IconCalendar", activity.IconKey);
        var workload = Assert.Single(snapshot.DoctorLoads!);
        Assert.Equal("دکتر رضایی", workload.Name);
        Assert.Equal(2, workload.Done);
        Assert.Equal(5, workload.Total);
        Assert.Equal("گربه", Assert.Single(snapshot.Species!).Name);
    }

    // DashboardMapping is internal to Client.Services; Application.Tests has InternalsVisibleTo.
    private static Contracts.Dashboard.DashboardSnapshot InvokeMap(
        UserRole role, Application.UseCases.Dashboard.DashboardSlice slice) =>
        DashboardMapping.Map(role, slice);
}
