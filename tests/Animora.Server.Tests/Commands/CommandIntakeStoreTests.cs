using Animora.Contracts.Wire.Commands;
using Animora.Domain.Primitives;
using Animora.Persistence.PostgreSql.Commands;
using Animora.Server.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Animora.Server.Tests.Commands;

/// <summary>Real PostgreSQL cases. PgFact skips with an explicit reason when no connection is configured.</summary>
public sealed class CommandIntakeStoreTests(PgFixture pg) : IClassFixture<PgFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [PgFact]
    public async Task OnConflictDoNothing_ConcurrentSameKey_SingleWinner()
    {
        string tenantId = IdGenerator.NewId();
        string operationId = IdGenerator.NewId();
        var tenant = new FixedTenantContext(tenantId);
        await using (var schema = TestSupport.CreateContext(tenant, pg.ConnectionString))
            await schema.Database.MigrateAsync();
        Task<IntakeBeginResult>[] attempts = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = TestSupport.CreateContext(tenant, pg.ConnectionString);
            var store = new CommandIntakeStore(db, tenant);
            return await store.TryBeginAsync(Row(tenantId, operationId));
        }).ToArray();
        IntakeBeginResult[] results = await Task.WhenAll(attempts);
        Assert.Single(results, r => r.Inserted);
        Assert.All(results.Where(r => !r.Inserted), r => Assert.Equal(operationId, r.Existing!.OperationId));
        await using var verify = TestSupport.CreateContext(tenant, pg.ConnectionString);
        Assert.Equal(1, await verify.CommandIntake.CountAsync(r => r.OperationId == operationId));
    }

    [PgFact]
    public async Task Settled_IffEffectCommitted_RollbackLeavesInProgress()
    {
        string tenantId = IdGenerator.NewId();
        string operationId = IdGenerator.NewId();
        string entityId = IdGenerator.NewId();
        var tenant = new FixedTenantContext(tenantId);
        await using var db = TestSupport.CreateContext(tenant, pg.ConnectionString);
        await db.Database.MigrateAsync();
        // A surrogate effect table in this isolated test database, not production schema.
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS public.\"IntakeTestEffects\" (\"OperationId\" varchar(26) PRIMARY KEY)");
        var store = new CommandIntakeStore(db, tenant);
        CommandIntakeRecord row = Row(tenantId, operationId);
        Assert.True((await store.TryBeginAsync(row)).Inserted);
        var settlement = Receipt(row, entityId);
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlAsync($"INSERT INTO public.\"IntakeTestEffects\" (\"OperationId\") VALUES ({operationId})");
            Assert.True((await store.CompleteAsync(settlement)).IsSuccess);
            await tx.RollbackAsync();
        }
        Assert.Equal(IntakeStatus.InProgress, (await store.FindAsync(operationId))!.Status);
        Assert.Empty(await store.GetIdMapAsync(operationId));
        Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM public.\"IntakeTestEffects\" WHERE \"OperationId\" = {operationId}").SingleAsync());
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlAsync($"INSERT INTO public.\"IntakeTestEffects\" (\"OperationId\") VALUES ({operationId})");
            Assert.True((await store.CompleteAsync(settlement)).IsSuccess);
            await tx.CommitAsync();
        }
        Assert.Equal(IntakeStatus.Settled, (await store.FindAsync(operationId))!.Status);
        Assert.Equal(entityId, Assert.Single(await store.GetIdMapAsync(operationId)).ServerEntityId);
        Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM public.\"IntakeTestEffects\" WHERE \"OperationId\" = {operationId}").SingleAsync());
    }

    [PgFact]
    public async Task Compaction_KeepsFingerprintAndOutcome()
    {
        string tenantId = IdGenerator.NewId();
        var tenant = new FixedTenantContext(tenantId);
        await using var db = TestSupport.CreateContext(tenant, pg.ConnectionString);
        await db.Database.MigrateAsync();
        var store = new CommandIntakeStore(db, tenant);
        CommandIntakeRecord row = Row(tenantId, IdGenerator.NewId());
        string entityId = IdGenerator.NewId();
        await store.TryBeginAsync(row);
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            Assert.True((await store.CompleteAsync(Receipt(row, entityId))).IsSuccess);
            await tx.CommitAsync();
        }
        var before = await store.FindAsync(row.OperationId);
        var compactor = new CommandIntakeCompactor(db);
        Assert.True(await compactor.CompactAsync(Now.AddDays(121)) >= 1);
        Assert.Equal(0, await compactor.CompactAsync(Now.AddDays(121)));
        var after = await store.FindAsync(row.OperationId);
        Assert.Null(after!.PayloadJson);
        Assert.Equal(before!.Fingerprint, after.Fingerprint);
        Assert.Equal(before.Outcome, after.Outcome);
        Assert.Equal(before.SettledUtc, after.SettledUtc);
        Assert.Equal(entityId, Assert.Single(await store.GetIdMapAsync(row.OperationId)).ServerEntityId);
    }

    [PgFact]
    public async Task OldAttempt_CannotSettle_AfterTakeover()
    {
        string tenantId = IdGenerator.NewId();
        var tenant = new FixedTenantContext(tenantId);
        await using var db = TestSupport.CreateContext(tenant, pg.ConnectionString);
        await db.Database.MigrateAsync();
        var store = new CommandIntakeStore(db, tenant);
        CommandIntakeRecord row = Row(tenantId, IdGenerator.NewId(), Now.AddSeconds(-1));
        await store.TryBeginAsync(row);
        string nextAttempt = IdGenerator.NewId();
        Assert.True(await store.TryAcquireAttemptAsync(row.OperationId, row.AttemptId, nextAttempt, "recovery", Now, Now.AddMinutes(1)));
        await using var tx = await db.Database.BeginTransactionAsync();
        Assert.True((await store.CompleteAsync(Receipt(row, IdGenerator.NewId()))).IsFailure);
        await tx.RollbackAsync();
        Assert.Equal(nextAttempt, (await store.FindAsync(row.OperationId))!.AttemptId);
    }

    private static CommandIntakeRecord Row(string tenantId, string operationId, DateTimeOffset? leaseUntil = null) =>
        CommandIntakeRecord.Begin(tenantId, operationId, "test.create", new string('a', 64), "{}", null,
            IdGenerator.NewId(), IdGenerator.NewId(), "test", Now, leaseUntil ?? Now.AddMinutes(1));

    private static IntakeSettlement Receipt(CommandIntakeRecord row, string entityId) => new(
        row.OperationId, row.AttemptId!, (short)SettlementOutcome.Accepted, null, "ruleset=1", entityId,
        null, (short)FinancialEffect.None, null, Now, [new CommandIdMapItem(0, "Test", entityId)]);
}
