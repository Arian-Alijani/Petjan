using Animora.Application.Abstractions;
using Animora.Application.UseCases.Scheduling;
using Animora.Application.UseCases.Scheduling.ReadModels;
using Animora.Application.Ports;
using Animora.Domain.Results;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Scheduling;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

public sealed class AppointmentQueryTests
{
    private sealed class TehranZone : IClinicTimeZoneProvider
    {
        public TimeZoneInfo GetClinicTimeZone() => TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("م")]
    public async Task PatientSearch_ShortInputDoesNotAccessDatabase(string search)
    {
        var queries = new AppointmentQueries(null!);
        Assert.Empty(await queries.GetPatientsAsync(search, "all", 21, null, null, default));
    }

    [Fact]
    public async Task PatientSearch_HonorsCancellationBeforeDatabaseAccess()
    {
        var queries = new AppointmentQueries(null!);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queries.GetPatientsAsync(
            "میلو", "all", 21, null, null, cancellation.Token));
    }

    [Fact]
    public async Task PatientSearch_IsBoundedNormalizedAndFindsRecordsBeyondOldWindow()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var owner = Owner.Register(fixture.TenantId, "کیان", "09121234567", fixture.Clock);
        db.Add(owner);
        for (var i = 0; i < 505; i++)
            db.Add(Pet.Register(fixture.TenantId, owner.Id, $"Alpha{i:D3}", PetSpecies.Dog, fixture.Clock));
        var target = Pet.Register(fixture.TenantId, owner.Id, "Zebra", PetSpecies.Cat, fixture.Clock);
        db.Add(target);
        var appointment = Appointment.Book(fixture.TenantId, target.Id, owner.Id, "GROOMING",
            new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero), 45,
            AppointmentSource.Phone, "reception", fixture.Clock).Value!;
        db.Add(appointment);
        await db.SaveChangesAsync();
        var queries = new AppointmentQueries(db);

        // Huge caller limits cannot inflate the bounded projection.
        Assert.Equal(21, (await queries.GetPatientsAsync("كيان", "all", 5000, null, null, default)).Count);
        Assert.Empty(await queries.GetPatientsAsync("absent", "all", 21, null, null, default));
        var found = Assert.Single(await queries.GetPatientsAsync("Zebra كيان", "cat", 21, null, null, default));
        Assert.Equal(target.Id, found.Id);
        Assert.Empty(await queries.GetPatientsAsync("Zebra", "dog", 21, null, null, default));
        Assert.Equal(target.Id, Assert.Single(await queries.GetPatientsAsync(
            "۰۹۱۲۱۲۳۴۵۶۷", "cat", 21, null, null, default)).Id);
        Assert.Equal(target.Id, Assert.Single(await queries.GetPatientsAsync(
            "", "all", 21, target.Id, null, default)).Id);
        Assert.Equal(target.Id, Assert.Single(await queries.GetPatientsAsync(
            "", "all", 21, null, appointment.Id, default)).Id);
    }

    [Fact]
    public async Task RangeOverSixtyTwoDays_ReturnsValidationCode()
    {
        var query = new AppointmentRangeQueryHandler(null!, new TehranZone());
        var result = await query.HandleAsync(new AppointmentRangeQuery(new DateOnly(2026, 3, 1),
            new DateOnly(2026, 6, 1)), new("T-LOCAL-DEFAULT", "user", "correlation"), default);
        Assert.Equal(ErrorCodes.AppointmentRangeTooWide, result.Error);
    }

    [Fact]
    public async Task LocalDayCounts_AndSqlRangeUseUtcIndexWithoutDateFunction()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var owner = Owner.Register(fixture.TenantId, "Owner", "09120000000", fixture.Clock);
        var pet = Pet.Register(fixture.TenantId, owner.Id, "Pet", PetSpecies.Dog, fixture.Clock);
        var doctor = User.Create(fixture.TenantId, "doctor", "Doctor", fixture.Clock);
        db.AddRange(owner, pet, doctor);
        var start = new DateTimeOffset(2026, 3, 1, 23, 0, 0, TimeSpan.Zero);
        db.Add(Appointment.Book(fixture.TenantId, pet.Id, owner.Id, "CHECKUP", start, 30,
            AppointmentSource.Phone, doctor.Id, fixture.Clock, doctor.Id).Value!);
        await db.SaveChangesAsync();
        var queries = new AppointmentQueries(db);
        var zone = new TehranZone();
        var counts = await new AppointmentDayCountsQueryHandler(queries, zone).HandleAsync(
            new AppointmentDayCountsQuery(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 7)),
            new(fixture.TenantId, doctor.Id, "corr"), default);
        Assert.Equal(1, counts.Value![new DateOnly(2026, 3, 2)]);

        var sql = db.Set<Appointment>().AsNoTracking()
            .Where(a => a.StartUtc >= start && a.StartUtc < start.AddDays(1))
            .ToQueryString().ToLowerInvariant();
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(sql, @"\bselect\b").Cast<object>());
        Assert.DoesNotContain("strftime", sql);
        Assert.DoesNotContain("date(", sql);
    }

    [Fact]
    public async Task History_FiltersPagesAndAggregatesInSql_BeyondAnyPreloadWindow()
    {
        using var fixture = new LocalStoreTestFixture(); // clock: 2026-03-01 09:00Z
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var owner = Owner.Register(fixture.TenantId, "Owner", "09120000000", fixture.Clock);
        var pet = Pet.Register(fixture.TenantId, owner.Id, "Milo", PetSpecies.Dog, fixture.Clock);
        var doctor = User.Create(fixture.TenantId, "doctor", "Doctor", fixture.Clock);
        db.AddRange(owner, pet, doctor);
        Appointment Make(int daysAgo, bool cancel)
        {
            var a = Appointment.Book(fixture.TenantId, pet.Id, owner.Id, "CHECKUP",
                fixture.Clock.Now.AddDays(-daysAgo), 30, AppointmentSource.Phone, doctor.Id, fixture.Clock, doctor.Id).Value!;
            if (cancel) Assert.True(a.Cancel("test", fixture.Clock).IsSuccess);
            db.Add(a);
            return a;
        }
        Make(120, cancel: true);   // far outside any +-40 day preload
        Make(10, cancel: true);
        Make(3, cancel: true);
        Make(2, cancel: false);    // still Scheduled: never part of history
        await db.SaveChangesAsync();

        var handler = new AppointmentHistoryQueryHandler(new AppointmentQueries(db), new TehranZone(), fixture.Clock);
        var ctx = new QueryContext(fixture.TenantId, doctor.Id, "corr");

        var all = (await handler.HandleAsync(new AppointmentHistoryQuery(), ctx, default)).Value!;
        Assert.Equal(3, all.Summary.Total);
        Assert.Equal(3, all.Summary.Cancelled);
        Assert.Equal(3, all.Items.Count);
        Assert.True(all.Items[0].StartUtcTicks > all.Items[2].StartUtcTicks); // newest first

        var page = (await handler.HandleAsync(new AppointmentHistoryQuery(Page: 0, PageSize: 2), ctx, default)).Value!;
        Assert.Equal(2, page.Items.Count);
        Assert.True(page.HasMore);
        Assert.Equal(3, page.Summary.Total); // totals cover the scope, not the page

        var recent = (await handler.HandleAsync(new AppointmentHistoryQuery(
            From: new DateOnly(2026, 2, 15)), ctx, default)).Value!;
        Assert.Equal(2, recent.Summary.Total);

        var byDoctor = (await handler.HandleAsync(new AppointmentHistoryQuery(DoctorId: "nobody"), ctx, default)).Value!;
        Assert.Equal(0, byDoctor.Summary.Total);
        var searched = (await handler.HandleAsync(new AppointmentHistoryQuery(Search: "milo"), ctx, default)).Value!;
        Assert.Equal(3, searched.Summary.Total);
        var unpaid = (await handler.HandleAsync(new AppointmentHistoryQuery(Invoice: HistoryInvoiceFilter.Paid), ctx, default)).Value!;
        Assert.Equal(0, unpaid.Summary.Total);
        var none = (await handler.HandleAsync(new AppointmentHistoryQuery(Invoice: HistoryInvoiceFilter.None), ctx, default)).Value!;
        Assert.Equal(3, none.Summary.Total);
    }
}
