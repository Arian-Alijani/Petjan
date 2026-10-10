using System.Text.Json;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Persistence.PostgreSql.Files;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Server.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
namespace Animora.Server.Tests.Admin;
public sealed class FileMismatchServiceTests(PgFixture pg) : IClassFixture<PgFixture>
{
    private static readonly ActorRef Actor = new(ActorKind.Operator, "01K00000000000000000000090");
    private static readonly TimeProvider Time = TimeProvider.System;
    private AnimoraOperatorDbContext Context(bool failAudit = false)
    {
        var options = new DbContextOptionsBuilder<AnimoraOperatorDbContext>().UseNpgsql(pg.ConnectionString);
        if (failAudit) options.AddInterceptors(new FailAudit());
        return new(options.Options);
    }
    private async Task<(string Tenant, string File)> Seed(FileState state)
    {
        await using (var schema = TestSupport.CreateContext(connection: pg.ConnectionString)) await schema.Database.MigrateAsync();
        string tenant = IdGenerator.NewId(), file = IdGenerator.NewId();
        await using var db = Context();
        db.FileObjects.Add(new() { TenantId = tenant, Id = file, State = state, Sha256 = new string('a', 64), SizeBytes = 123,
            DeclaredSizeBytes = 123, OriginalName = "/secret/never-render-this", ContentType = "text/plain",
            CreatedByUserId = "01K00000000000000000000091", CreatedUtc = Time.GetUtcNow(), UpdatedUtc = Time.GetUtcNow() });
        await db.SaveChangesAsync(); return (tenant, file);
    }
    [PgFact] public async Task RequeueAndAbandon_OnlyMismatch_SameTransactionAudit_AndSafeProjection()
    {
        foreach (bool abandon in new[] { false, true })
        {
            var (tenant, file) = await Seed(FileState.Mismatch);
            await using var db = Context(); var service = new FileMismatchService(db, Time);
            var result = abandon ? await service.AbandonAsync(tenant, file, Actor) : await service.RequeueAsync(tenant, file, Actor);
            Assert.True(result.IsSuccess);
            var row = await db.FileObjects.AsNoTracking().SingleAsync(x => x.TenantId == tenant && x.Id == file);
            Assert.Equal(abandon ? FileState.Abandoned : FileState.Uploaded, row.State);
            var audit = await db.Audit.AsNoTracking().SingleAsync(x => x.TargetTenantId == tenant);
            Assert.Equal(abandon ? "files.mismatch.abandon" : "files.mismatch.requeue", audit.Action); Assert.Equal(Actor.ActorId, audit.OperatorId);
            Assert.Equal(file, JsonDocument.Parse(audit.DetailJson).RootElement.GetProperty("fileId").GetString());
            Assert.DoesNotContain("/secret", audit.DetailJson);
            Assert.Equal(ErrorCodes.FileHashMismatch, (await service.RequeueAsync(tenant, file, Actor)).Error);
            Assert.Equal(1, await db.Audit.CountAsync(x => x.TargetTenantId == tenant));
        }
        var mismatch = await Seed(FileState.Mismatch);
        await using var read = Context(); var page = await new FileMismatchService(read, Time).ListAsync(1, 1000);
        Assert.Equal(200, page.Size); var safe = Assert.Single(page.Items.Where(x => x.Id == mismatch.File));
        Assert.Equal(8, safe.ShaPrefix?.Length); Assert.DoesNotContain("/secret", JsonSerializer.Serialize(safe)); Assert.Empty(read.ChangeTracker.Entries());
    }
    [PgFact] public async Task ConcurrentOperators_OnlyOneTransitionAndOneAudit()
    {
        var (tenant, file) = await Seed(FileState.Mismatch);
        await using var a = Context(); await using var b = Context();
        var results = await Task.WhenAll(new FileMismatchService(a, Time).RequeueAsync(tenant, file, Actor),
            new FileMismatchService(b, Time).AbandonAsync(tenant, file, Actor));
        Assert.Single(results.Where(x => x.IsSuccess)); Assert.Single(results.Where(x => x.Error == ErrorCodes.FileHashMismatch));
        await using var verify = Context(); Assert.Equal(1, await verify.Audit.CountAsync(x => x.TargetTenantId == tenant));
    }
    private sealed class FailAudit : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Synthetic audit failure.");
    }
    [PgFact] public async Task AuditFailure_RollsBackStateChange()
    {
        var (tenant, file) = await Seed(FileState.Mismatch);
        await using (var db = Context(true))
            await Assert.ThrowsAsync<InvalidOperationException>(() => new FileMismatchService(db, Time).RequeueAsync(tenant, file, Actor));
        await using var verify = Context();
        Assert.Equal(FileState.Mismatch, (await verify.FileObjects.AsNoTracking().SingleAsync(x => x.TenantId == tenant && x.Id == file)).State);
        Assert.Equal(0, await verify.Audit.CountAsync(x => x.TargetTenantId == tenant));
    }
}
