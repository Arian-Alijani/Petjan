using Animora.Application.UseCases.Sales;
using Animora.Domain.Sales;
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
namespace Animora.Server.Tests.HandlersS09;

public sealed class S09MigrationTests(PgFixture pg) : IClassFixture<PgFixture>
{
    [PgFact]
    public async Task Applies_OnTopOfPreviousHead()
    {
        await using var db = TestSupport.CreateContext(new FixedTenantContext(IdGenerator.NewId()), pg.ConnectionString);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261009180000_106_SchedulingBoarding");
        await migrator.MigrateAsync("20261010090000_107_SalesCashier");
        Assert.Contains("20261010090000_107_SalesCashier", await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Set<Insurer>().AsNoTracking().ToListAsync());
    }
}
public sealed class S09EndToEndTests(PgFixture pg) : IClassFixture<PgFixture>
{
    [PgFact]
    public async Task CommandsRoundTrip_ThroughIntake_WithTenantIsolation()
    {
        await using var a = new PgCommandHarness(pg.ConnectionString);
        await using var b = new PgCommandHarness(pg.ConnectionString);
        await a.Db.MigrateAndSeedAsync(a.Intake.TenantId, b.Intake.TenantId);
        var command = new CreateInsurerCommand("Insurer", 10, 0);
        var ea = a.Envelope(command); var eb = b.Envelope(command);
        var ra = await a.Service.SubmitAsync(ea); var rb = await b.Service.SubmitAsync(eb);
        Assert.Equal(SettlementOutcome.Accepted, ra.Value!.Outcome); Assert.Equal(SettlementOutcome.Accepted, rb.Value!.Outcome);
        Assert.Equal(ra.Value, (await a.Service.SubmitAsync(ea)).Value);
        Assert.Equal(rb.Value, (await b.Service.SubmitAsync(eb)).Value);
        Assert.NotNull(await a.Db.Set<Insurer>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == ra.Value.ServerEntityId));
        Assert.Null(await a.Db.Set<Insurer>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == rb.Value.ServerEntityId));
        Assert.Null(await b.Db.Set<Insurer>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == ra.Value.ServerEntityId));
    }
}
