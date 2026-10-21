using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.UseCases.Vaccination;
using Animora.Domain.Clinical;
using Animora.Domain.Patients;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Queries;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// <see cref="TreatmentCalendarQuery"/> is the single read path for «what is due»: it must return both the days of a
/// treatment plan and the next-dose reminders derived from administered vaccination records, so the «دارو و واکسن»
/// screen and the appointment calendar can never disagree about a due dose.
/// </summary>
public sealed class TreatmentCalendarQueryTests
{
    private sealed class TehranZone : IClinicTimeZoneProvider
    {
        public TimeZoneInfo GetClinicTimeZone() => TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
    }

    private static readonly DateTimeOffset Administered = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Calendar_IncludesTheNextDoseReminderOfAnAdministeredVaccine()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var record = await SeedDoseAsync(db, fixture, "میلو", "DHPP", "دی‌اچ‌پی‌پی");

        var rows = await QueryAsync(db, fixture, DueDay(record));

        var booster = Assert.Single(rows);
        Assert.Equal(TreatmentCalendarRowSource.VaccineBooster, booster.Source);
        Assert.Equal(record.Id, booster.OccurrenceId);
        Assert.Equal("دی‌اچ‌پی‌پی", booster.VaccineName);
        Assert.Equal(1, booster.DoseNumber);
        Assert.Equal(TreatmentItemType.Vaccine, booster.ItemType);
        Assert.Equal(TreatmentOccurrenceState.Pending, booster.State);
        Assert.Equal("میلو", booster.PetName);
    }

    [Fact]
    public async Task Calendar_MedicationOnlyFilter_HidesNextDoseReminders()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var record = await SeedDoseAsync(db, fixture, "میلو", "DHPP", "دی‌اچ‌پی‌پی");
        var day = DueDay(record);

        Assert.Empty(await QueryAsync(db, fixture, day, itemType: TreatmentItemType.Medication));
        Assert.Single(await QueryAsync(db, fixture, day, itemType: TreatmentItemType.Vaccine));
    }

    [Fact]
    public async Task Calendar_KindFilterWithoutClinicVisit_HidesNextDoseReminders()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var record = await SeedDoseAsync(db, fixture, "میلو", "DHPP", "دی‌اچ‌پی‌پی");
        var day = DueDay(record);

        Assert.Empty(await QueryAsync(db, fixture, day, kinds: [TreatmentKind.HomeByOwner]));
        Assert.Single(await QueryAsync(db, fixture, day, kinds: [TreatmentKind.ClinicVisit]));
    }

    [Fact]
    public async Task Calendar_PetFilter_ExcludesOtherPatientsReminders()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var record = await SeedDoseAsync(db, fixture, "میلو", "DHPP", "دی‌اچ‌پی‌پی");
        var day = DueDay(record);

        Assert.Single(await QueryAsync(db, fixture, day, petId: record.PetId));
        Assert.Empty(await QueryAsync(db, fixture, day, petId: "01OTHERPET0000000000000000"));
    }

    [Fact]
    public async Task Calendar_ExcludesAWindowWithoutReminders()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var record = await SeedDoseAsync(db, fixture, "میلو", "DHPP", "دی‌اچ‌پی‌پی");

        Assert.Empty(await QueryAsync(db, fixture, DueDay(record).AddDays(30)));
    }

    private static async Task<IReadOnlyList<TreatmentCalendarRow>> QueryAsync(AnimoraLocalDbContext db,
        LocalStoreTestFixture fixture, DateOnly day, IReadOnlyCollection<TreatmentKind>? kinds = null,
        TreatmentItemType? itemType = null, string? petId = null)
    {
        var handler = new TreatmentCalendarQueryHandler(
            new TreatmentPlanQueries(db), new VaccinationQueries(db), new TehranZone());
        var result = await handler.HandleAsync(
            new TreatmentCalendarQuery(day, day, kinds, itemType, petId),
            new QueryContext(fixture.TenantId, "user-test", "corr-test"),
            default);
        Assert.True(result.IsSuccess, result.Detail);
        return result.Value!;
    }

    /// <summary>The clinic-local day the record's next dose falls on — the same conversion the query performs.</summary>
    private static DateOnly DueDay(VaccinationRecord record) =>
        Animora.Application.Time.ClinicCalendar.LocalDay(
            record.NextDueUtc!.Value.UtcTicks, TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran"));

    private static async Task<VaccinationRecord> SeedDoseAsync(AnimoraLocalDbContext db, LocalStoreTestFixture fixture,
        string petName, string vaccineCode, string vaccineName)
    {
        var owner = Owner.Register(fixture.TenantId, "کیان", "09121234567", fixture.Clock);
        db.Add(owner);
        var pet = Pet.Register(fixture.TenantId, owner.Id, petName, PetSpecies.Dog, fixture.Clock);
        db.Add(pet);
        var record = VaccinationRecord.Administer(fixture.TenantId, pet.Id, vaccineCode, vaccineName, 1,
            Administered, "user-test", fixture.Clock).Value!;
        db.Add(record);
        await db.SaveChangesAsync();
        return record;
    }
}
