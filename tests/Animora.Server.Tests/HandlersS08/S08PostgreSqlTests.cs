using Animora.Application.UseCases.Boarding;
using Animora.Application.UseCases.Scheduling;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Boarding;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Server.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;
namespace Animora.Server.Tests.HandlersS08;

public sealed class S08MigrationTests(PgFixture pg) : IClassFixture<PgFixture>
{
    [PgFact]
    public async Task Applies_OnTopOfPreviousHead()
    {
        await using var db = TestSupport.CreateContext(new FixedTenantContext(IdGenerator.NewId()), pg.ConnectionString);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261009170000_105_ClinicalCore");
        await migrator.MigrateAsync("20261009180000_106_SchedulingBoarding");
        Assert.Contains("20261009180000_106_SchedulingBoarding", await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Set<Room>().AsNoTracking().ToListAsync());
    }
}
public sealed class S08EndToEndTests(PgFixture pg) : IClassFixture<PgFixture>
{
    [PgFact]
    public async Task CommandsRoundTrip_ThroughIntake_WithTenantIsolation()
    {
        await using var a = new PgCommandHarness(pg.ConnectionString);
        await using var b = new PgCommandHarness(pg.ConnectionString);
        await a.Db.Database.MigrateAsync();
        var command = new CreateRoomCommand("R1", "Room", RoomType.Standard, 1, null);
        var ea = a.Envelope(command); var eb = b.Envelope(command);
        var ra = await a.Service.SubmitAsync(ea); var rb = await b.Service.SubmitAsync(eb);
        Assert.Equal(SettlementOutcome.Accepted, ra.Value!.Outcome); Assert.Equal(SettlementOutcome.Accepted, rb.Value!.Outcome);
        Assert.Equal(ra.Value, (await a.Service.SubmitAsync(ea)).Value);
        Assert.Equal(rb.Value, (await b.Service.SubmitAsync(eb)).Value);
        Assert.NotNull(await a.Db.Set<Room>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == ra.Value.ServerEntityId));
        Assert.Null(await a.Db.Set<Room>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == rb.Value.ServerEntityId));
        Assert.Null(await b.Db.Set<Room>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == ra.Value.ServerEntityId));
    }
}
public sealed class S08ConcurrencyTests(PgFixture pg) : IClassFixture<PgFixture>
{
    [PgFact]
    public async Task TwoDevices_SameSlot_OneWins()
    {
        var tenant = IdGenerator.NewId(); var doctor = IdGenerator.NewId();
        await using var a = new PgCommandHarness(pg.ConnectionString, tenant);
        await using var b = new PgCommandHarness(pg.ConnectionString, tenant);
        await a.Db.Database.MigrateAsync();
        var owner = Owner.Register(tenant, "Owner", "09120000000", a.Intake.Clock);
        var pet = Pet.Register(tenant, owner.Id, "Pet", PetSpecies.Dog, a.Intake.Clock);
        var membership = StaffMembership.Join(tenant, doctor, StaffRole.Doctor, a.Intake.Clock);
        membership.SetWorkingHours(480, 1200, 127);
        a.Db.AddRange(owner, pet, membership); await a.Db.SaveChangesAsync(); a.Db.ChangeTracker.Clear();
        var command = new BookAppointmentCommand(new DateOnly(2026, 10, 9), 600, 30, "CHECKUP", doctor, pet.Id, owner.Id, null);
        var results = await Task.WhenAll(a.Service.SubmitAsync(a.Envelope(command)), b.Service.SubmitAsync(b.Envelope(command)));
        Assert.Single(results, r => r.Value?.Outcome == SettlementOutcome.Accepted);
        var rejected = Assert.Single(results, r => r.Value?.Outcome == SettlementOutcome.Rejected);
        Assert.Equal(ErrorCodes.AppointmentSlotConflict.Code, rejected.Value!.ErrorCode);
        Assert.Equal(1, await a.Db.Set<Animora.Domain.Scheduling.Appointment>().AsNoTracking().CountAsync());
    }
    [PgFact]
    public async Task TwoDevices_LastRoomPlace_OneWins()
    {
        var tenant = IdGenerator.NewId();
        await using var a = new PgCommandHarness(pg.ConnectionString, tenant);
        await using var b = new PgCommandHarness(pg.ConnectionString, tenant);
        await a.Db.Database.MigrateAsync();
        var owner = Owner.Register(tenant, "Owner", "09120000000", a.Intake.Clock);
        var pet = Pet.Register(tenant, owner.Id, "Pet", PetSpecies.Dog, a.Intake.Clock);
        var room = Room.Create(tenant, "R1", "Room", RoomType.Standard, 1).Value!;
        a.Db.AddRange(owner, pet, room); await a.Db.SaveChangesAsync(); a.Db.ChangeTracker.Clear();
        var command = new CreateBoardingStayCommand(pet.Id, room.Id, new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 11), 100, null);
        var results = await Task.WhenAll(a.Service.SubmitAsync(a.Envelope(command)), b.Service.SubmitAsync(b.Envelope(command)));
        Assert.Single(results, r => r.Value?.Outcome == SettlementOutcome.Accepted);
        var rejected = Assert.Single(results, r => r.Value?.Outcome == SettlementOutcome.Rejected);
        Assert.Equal(ErrorCodes.RoomAtCapacity.Code, rejected.Value!.ErrorCode);
        Assert.Equal(1, await a.Db.Set<BoardingStay>().AsNoTracking().CountAsync());
    }
}
