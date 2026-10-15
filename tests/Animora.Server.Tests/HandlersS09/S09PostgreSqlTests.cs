using Animora.Application.UseCases.Sales;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using Animora.Persistence.PostgreSql.Repositories;
using Animora.Persistence.PostgreSql.Commands;
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

/// <summary>D04/D12: exercise the actual PostgreSQL read/UNION queries, not an in-memory provider.</summary>
public sealed class FinancialPeerLedgerTests(PgFixture pg) : IClassFixture<PgFixture>
{
    [PgFact]
    public async Task FinancialLedger_IsTenantBound_AndPreservesPostedAndVoidTimes()
    {
        await using var a = new PgCommandHarness(pg.ConnectionString);
        await using var b = new PgCommandHarness(pg.ConnectionString);
        await a.Db.MigrateAndSeedAsync(a.Intake.TenantId, b.Intake.TenantId);
        var postedId = IdGenerator.NewId();
        var invoice = Invoice.Create(a.Intake.TenantId, "INV-" + postedId, InvoiceKind.DirectSale,
            a.Intake.Clock.UtcNow, a.Intake.UserId, id: postedId).Value!;
        invoice.AddLine(new InvoiceLine("L1", InvoiceLineKind.Service, null, null, null, "Exam",
            new Quantity(1m, "PCS"), Money.Irt(1000), 0m, 0m, InvoiceLineSource.Manual, null));
        Assert.True(invoice.Post(a.Intake.Clock).IsSuccess);
        var posted = invoice.PostedUtc;
        var cancelled = a.Intake.Clock.UtcNow.AddHours(2);
        a.Intake.Clock.UtcNow = cancelled;
        invoice.AddCreditNote(new InvoiceAdjustment(IdGenerator.NewId(), AdjustmentKind.CreditNote,
            Money.Irt(1000), "cancel", cancelled, a.Intake.UserId, IdGenerator.NewId(), null));
        Assert.True(invoice.Void("cancel", a.Intake.Clock).IsSuccess);
        a.Db.Add(invoice);
        await a.Db.SaveChangesAsync();
        a.Db.ChangeTracker.Clear();

        var ledger = await new FinanceLedgerReader(a.Db).GetLedgerSinceAsync(0, 10, default);
        var row = Assert.Single(ledger.Invoices);
        Assert.Equal(postedId, row.Id);
        Assert.Equal(posted, row.PostedUtc);
        Assert.Equal(cancelled, row.VoidedUtc);
        Assert.Empty((await new FinanceLedgerReader(b.Db).GetLedgerSinceAsync(0, 10, default)).Invoices);
        var history = await new SalesQueries(a.Db).GetHistoryAsync(null, null, null, null, "Oldest", 1, 10, default);
        Assert.Equal(posted, Assert.Single(history.Page.Items, r => r.Kind == "Accepted").OccurredUtc);
        Assert.Equal(cancelled, Assert.Single(history.Page.Items, r => r.Kind == "Void").OccurredUtc);
        Assert.Equal(0, history.InflowMinor);
    }
    [PgFact]
    public async Task FinancialLedger_DraftVoidUsesRetainedAcceptedReceipt_NotIssueOrTransportTime()
    {
        await using var a = new PgCommandHarness(pg.ConnectionString);
        await a.Db.MigrateAndSeedAsync(a.Intake.TenantId);
        var id = IdGenerator.NewId();
        var invoice = Invoice.Create(a.Intake.TenantId, "INV-" + id, InvoiceKind.DirectSale,
            a.Intake.Clock.UtcNow, a.Intake.UserId, id: id).Value!;
        var voided = a.Intake.Clock.UtcNow.AddHours(1);
        a.Intake.Clock.UtcNow = voided;
        Assert.True(invoice.Void("cancel empty draft", a.Intake.Clock).IsSuccess);
        a.Db.Add(invoice);
        var receipt = CommandIntakeRecord.Begin(a.Intake.TenantId, IdGenerator.NewId(), CommandTypes.SalesInvoiceVoid,
            new string('a', 64), "{}", null, "corr", IdGenerator.NewId(), "test", voided, voided.AddMinutes(1));
        a.Db.Add(receipt);
        await a.Db.SaveChangesAsync();
        await a.Db.CommandIntake.Where(c => c.OperationId == receipt.OperationId).ExecuteUpdateAsync(u => u
            .SetProperty(c => c.Status, IntakeStatus.Settled).SetProperty(c => c.Outcome, (short?)SettlementOutcome.Accepted)
            .SetProperty(c => c.ServerEntityId, id).SetProperty(c => c.SettledUtc, (DateTimeOffset?)voided)
            .SetProperty(c => c.PayloadJson, (string?)null)); // the retained receipt still carries the event time
        a.Db.ChangeTracker.Clear();
        var row = Assert.Single((await new FinanceLedgerReader(a.Db).GetLedgerSinceAsync(0, 10, default)).Invoices);
        Assert.Null(row.PostedUtc);
        Assert.Equal(voided, row.VoidedUtc);
        var history = await new SalesQueries(a.Db).GetHistoryAsync(null, null, null, null, "Oldest", 1, 10, default);
        Assert.Equal("Void", Assert.Single(history.Page.Items).Kind);
        Assert.Equal(voided, history.Page.Items[0].OccurredUtc);
    }

}
