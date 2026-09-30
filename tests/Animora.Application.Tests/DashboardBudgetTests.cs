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
    [InlineData(UserRole.Manager, 9)]
    [InlineData(UserRole.Doctor, 5)]
    [InlineData(UserRole.Cashier, 7)]
    [InlineData(UserRole.Receptionist, 9)]
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
