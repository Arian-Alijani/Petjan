using Animora.Application.UseCases.Scheduling;
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
            AppointmentSource.Phone, null, fixture.Clock, "reception").Value!;
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
}
