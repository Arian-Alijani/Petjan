using Animora.Application.Auditing;
using Animora.Domain.Inventory;
using Animora.Domain.ValueObjects;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Persistence.PostgreSql.Tenancy;
using Animora.Server.Commands;
using Animora.Application.UseCases.Purchasing;
using Animora.Domain.Purchasing;
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
namespace Animora.Server.Tests.HandlersS10;

public sealed class S10MigrationTests(PgFixture pg) : IClassFixture<PgFixture>
{
    [PgFact]
    public async Task Applies_OnTopOfPreviousHead()
    {
        await using var db = TestSupport.CreateContext(new FixedTenantContext(IdGenerator.NewId()), pg.ConnectionString);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261010090000_107_SalesCashier");
        await migrator.MigrateAsync("20261010100000_108_MoneyStock");
        Assert.Contains("20261010100000_108_MoneyStock", await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Set<Supplier>().AsNoTracking().ToListAsync());
    }
}
public sealed class S10EndToEndTests(PgFixture pg) : IClassFixture<PgFixture>
{
    [PgFact]
    public async Task CommandsRoundTrip_ThroughIntake_WithTenantIsolation()
    {
        await using var a = new PgCommandHarness(pg.ConnectionString);
        await using var b = new PgCommandHarness(pg.ConnectionString);
        await a.Db.MigrateAndSeedAsync(a.Intake.TenantId, b.Intake.TenantId);
        var command = new CreateSupplierCommand("Supplier");
        var ea = a.Envelope(command); var eb = b.Envelope(command);
        var ra = await a.Service.SubmitAsync(ea); var rb = await b.Service.SubmitAsync(eb);
        Assert.Equal(SettlementOutcome.Accepted, ra.Value!.Outcome); Assert.Equal(SettlementOutcome.Accepted, rb.Value!.Outcome);
        Assert.Equal(ra.Value, (await a.Service.SubmitAsync(ea)).Value);
        Assert.Equal(rb.Value, (await b.Service.SubmitAsync(eb)).Value);
        Assert.NotNull(await a.Db.Set<Supplier>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == ra.Value.ServerEntityId));
        Assert.Null(await a.Db.Set<Supplier>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == rb.Value.ServerEntityId));
        Assert.Null(await b.Db.Set<Supplier>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == ra.Value.ServerEntityId));
    }
}

/// <summary>Regression for root + immutable variant replacement and child-trigger token rotation.</summary>
public sealed class AggregateChildConcurrencyTests(PgFixture pg) : IClassFixture<PgFixture>
{
    private AnimoraServerDbContext Context(string tenantId) => new(
        new DbContextOptionsBuilder<AnimoraServerDbContext>().UseNpgsql(pg.ConnectionString)
            .AddInterceptors(new NoUnnamedIgnoreFiltersInterceptor(new FixedTenantContext(tenantId))).Options,
        new FixedTenantContext(tenantId));

    private async Task<AnimoraServerDbContext> Seed()
    {
        var tenantId = IdGenerator.NewId();
        var db = Context(tenantId);
        await db.MigrateAndSeedAsync(tenantId);
        var variant = new ProductVariant(IdGenerator.NewId(), "variant", ProductVariant.DefaultAttributeName,
            "", Money.Irt(100), Money.Irt(80000), Quantity.Zero("PCS"), null, true);
        var product = Product.Create(tenantId, "product", "Product", "category", ProductKind.Medicine,
            "PCS", Money.Irt(100), Money.Irt(80000), [variant]).Value!;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        db.Add(product);
        await db.SaveChangesAsync();
        Assert.Equal(await StoreVersion(db, product.Id), product.Version);
        return db;
    }

    private static void Change(Product product, long price, bool root = true)
    {
        if (root) Assert.True(product.SetPrices(product.DefaultCostPrice, Money.Irt(price)).IsSuccess);
        Assert.True(product.UpdateVariant(product.Variants[0] with { SalePrice = Money.Irt(price) }).IsSuccess);
    }

    private static Task<string?> StoreVersion(AnimoraServerDbContext db, string id) =>
        db.ProductSet.AsNoTracking().Where(p => p.Id == id).Select(p => p.Version).SingleAsync();

    private static async Task AssertFinalToken(AnimoraServerDbContext db, Product product)
    {
        Assert.Equal(await StoreVersion(db, product.Id), product.Version);
        Assert.Equal(product.Version, db.Entry(product).Property(p => p.Version).OriginalValue);
        Assert.Equal(EntityState.Unchanged, db.Entry(product).State);
    }

    private static SecurityAuditEntry Audit(Product product) => SecurityAuditEntry.Create(
        IdGenerator.NewId(), product.TenantId, DateTimeOffset.UtcNow, "inventory", "price.update", "Success",
        IdGenerator.NewId(), null, null, null);

    [PgFact]
    public async Task PriceAndVariantReplacement_RepeatedSaves_KeepFinalTokenAndJournal()
    {
        await using var db = await Seed();
        var product = db.ProductSet.Local.Single();
        await using var tx = await db.Database.BeginTransactionAsync();
        foreach (var price in new long[] { 780000, 790000 })
        {
            var before = product.Version;
            Change(product, price);
            db.SecurityAudit.Add(Audit(product));
            await db.SaveChangesAsync();
            Assert.NotEqual(before, product.Version);
            await AssertFinalToken(db, product);
            // SaveChanges must not publish feed sequences before the outer commit.
            Assert.True(await db.TenantJournal.AnyAsync(j => j.EntityId == product.Id && j.ChangeSequence == null));
        }
        var final = product.Version;
        await tx.CommitAsync();
        var last = await db.TenantJournal.AsNoTracking().Where(j => j.EntityId == product.Id)
            .OrderByDescending(j => j.ChangeSequence).FirstAsync();
        Assert.Equal(Guid.Parse(final!), last.Version);
        Assert.Equal(2, await db.SecurityAudit.CountAsync());
        Assert.Equal(790000, (await db.ProductSet.AsNoTracking().SingleAsync()).Variants[0].SalePrice.Minor);
    }

    [PgFact]
    public async Task ChildOnlyAndRootOnlyEdits_SameContext_DoNotReuseStaleTokens()
    {
        await using var db = await Seed();
        var product = db.ProductSet.Local.Single();
        var before = product.Version;
        Change(product, 780000, root: false);
        await db.SaveChangesAsync();
        Assert.NotEqual(before, product.Version);
        await AssertFinalToken(db, product);
        Assert.True(product.SetPrices(product.DefaultCostPrice, Money.Irt(790000)).IsSuccess);
        await db.SaveChangesAsync();
        await AssertFinalToken(db, product);
    }

    [PgFact]
    public async Task StaleChildOnlyEdit_IsRealConcurrencyConflict_AndRollsBackAuditAndChildren()
    {
        await using var db = await Seed();
        var product = db.ProductSet.Local.Single();
        await using (var other = Context(product.TenantId))
        {
            var concurrent = await other.ProductSet.SingleAsync();
            Change(concurrent, 810000);
            await other.SaveChangesAsync();
        }
        await using var tx = await db.Database.BeginTransactionAsync();
        Change(product, 780000, root: false);
        db.SecurityAudit.Add(Audit(product));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
        // The SaveChanges savepoint, not an outer rollback, must undo ALL staged effects.
        db.ChangeTracker.Clear();
        var stored = await db.ProductSet.SingleAsync();
        Assert.Equal(810000, stored.DefaultSalePrice.Minor);
        Assert.Equal(810000, stored.Variants[0].SalePrice.Minor);
        Assert.Empty(await db.SecurityAudit.AsNoTracking().ToListAsync());
        // A deliberate subsequent edit still works in this transaction, proving mode cleanup.
        Change(stored, 820000);
        await db.SaveChangesAsync();
        await AssertFinalToken(db, stored);
        await tx.CommitAsync();
    }

    [PgFact]
    public async Task SynchronousSaveAndSaveWithoutAccepting_PreserveEfSemantics()
    {
        await using var db = await Seed();
        var product = db.ProductSet.Local.Single();
        Change(product, 780000);
        db.SaveChanges();
        await AssertFinalToken(db, product);
        var before = db.Entry(product).Property(p => p.Version).OriginalValue;
        Change(product, 790000);
        await db.SaveChangesAsync(acceptAllChangesOnSuccess: false);
        Assert.Equal(EntityState.Modified, db.Entry(product).State);
        Assert.Equal(before, db.Entry(product).Property(p => p.Version).OriginalValue);
        Assert.Equal(await StoreVersion(db, product.Id), product.Version);
        db.ChangeTracker.AcceptAllChanges();
        await AssertFinalToken(db, product);
        Change(product, 800000);
        db.SaveChanges();
        await AssertFinalToken(db, product);
    }

    [PgFact]
    public async Task DeferredCaptureFailure_RollsBackSavepointBeforeAuditOrEffectCanCommit()
    {
        await using var db = await Seed();
        var product = db.ProductSet.Local.Single();
        await using var tx = await db.Database.BeginTransactionAsync();
        // Fail ONLY the child-trigger's nested touch, not EF's own root UPDATE.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.fail_aggregate_touch() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF pg_trigger_depth() > 1 THEN RAISE EXCEPTION 'capture failure'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER zz_fail_aggregate_touch BEFORE UPDATE ON public."Products"
                FOR EACH ROW EXECUTE FUNCTION public.fail_aggregate_touch();
            """);
        Change(product, 780000);
        db.SecurityAudit.Add(Audit(product));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var stored = await db.ProductSet.SingleAsync();
        Assert.Equal(80000, stored.DefaultSalePrice.Minor);
        Assert.Equal(80000, stored.Variants[0].SalePrice.Minor);
        Assert.Empty(await db.SecurityAudit.AsNoTracking().ToListAsync());
        // The function/trigger were created before the savepoint; dropping them and retrying
        // proves the outer transaction was not aborted and capture modes were restored.
        await db.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER zz_fail_aggregate_touch ON public."Products";
            DROP FUNCTION public.fail_aggregate_touch();
            """);
        Change(stored, 790000);
        await db.SaveChangesAsync();
        await AssertFinalToken(db, stored);
        await tx.CommitAsync();
    }

    [PgFact]
    public async Task AddingAndDeletingChildren_RotatesFinalToken_AndOuterRollbackUndoesEverything()
    {
        await using var db = await Seed();
        var product = db.ProductSet.Local.Single();
        var original = product.Version;
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var added = product.Variants[0] with { VariantId = IdGenerator.NewId(), Sku = "second" };
            Assert.True(product.AddVariant(added).IsSuccess);
            await db.SaveChangesAsync();
            await AssertFinalToken(db, product);
            db.Remove(added);
            await db.SaveChangesAsync();
            await AssertFinalToken(db, product);
            await tx.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        var stored = await db.ProductSet.AsNoTracking().SingleAsync();
        Assert.Single(stored.Variants);
        Assert.Equal(original, stored.Version);
    }
}

public sealed class ServerCommandAuditEnlistmentTests
{
    [Fact]
    public async Task WritingCommandAudit_OnlyStagesRow_WithoutOpeningDatabase()
    {
        using var db = TestSupport.CreateContext(new FixedTenantContext("tenant"));
        var writer = new ServerCommandAuditWriter(db);
        var record = new AuditRecord("tenant", "user", "device", DateTimeOffset.UtcNow,
            AuditCategory.Inventory, "inventory.product.update", "Product", "product", "op", "corr",
            AuditOutcome.Success, null, null, null, null, null);
        Assert.True((await writer.WriteAsync(record)).IsSuccess);
        Assert.Equal(EntityState.Added, db.Entry(Assert.Single(db.SecurityAudit.Local)).State);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await writer.WriteAsync(record, cancellation.Token));
        Assert.Single(db.SecurityAudit.Local);
    }
}
