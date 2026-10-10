using System.Text.Json;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Outbox;
using Animora.Persistence.PostgreSql.Replication;
using Animora.Server.Tests.Persistence;
using Animora.Server.Tests.Sync;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace Animora.Server.Tests.Outbox;

public sealed class OutboxClaimTests(PgFixture pg) : IClassFixture<PgFixture>
{
    internal static OutboxDbContext Context(SyncPgRig rig) => new(new DbContextOptionsBuilder<OutboxDbContext>().UseNpgsql(rig.Source).Options);
    internal static async Task Seed(SyncPgRig rig, int count)
    {
        await using var db = Context(rig); var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < count; i++) db.Messages.Add(new() { TenantId = rig.Tenant, Id = IdGenerator.NewId(now),
            MessageType = "test.message", CreatedUtc = now, NextAttemptUtc = now.AddSeconds(-1), CorrelationId = "test" });
        await db.SaveChangesAsync();
    }
    [PgFact]
    public async Task TwoWorkers_NeverClaimSameRow_SkipLocked()
    {
        await using var rig = new SyncPgRig(pg.ConnectionString); await rig.Initialize(); await Seed(rig, 20);
        await using var a = Context(rig); await using var b = Context(rig);
        var claims = await Task.WhenAll(new OutboxClaimStore(a).ClaimAsync(DateTimeOffset.UtcNow, 20, CancellationToken.None),
            new OutboxClaimStore(b).ClaimAsync(DateTimeOffset.UtcNow, 20, CancellationToken.None));
        var ids = claims.SelectMany(x => x).Select(x => x.Id).ToArray();
        Assert.Equal(20, ids.Length); Assert.Equal(ids.Length, ids.Distinct().Count());
    }
    [PgFact]
    public async Task ExpiredLease_Reclaimable_FencedCompletionRejected()
    {
        await using var rig = new SyncPgRig(pg.ConnectionString); await rig.Initialize(); await Seed(rig, 1);
        await using var a = Context(rig); await using var b = Context(rig); var now = DateTimeOffset.UtcNow;
        var old = Assert.Single(await new OutboxClaimStore(a).ClaimAsync(now, 1, CancellationToken.None));
        var later = now + OutboxClaimStore.Lease + TimeSpan.FromSeconds(1);
        var fresh = Assert.Single(await new OutboxClaimStore(b).ClaimAsync(later, 1, CancellationToken.None));
        Assert.NotEqual(old.ClaimToken, fresh.ClaimToken);
        Assert.Equal(0, await new OutboxClaimStore(a).CompleteAsync(old, later, CancellationToken.None));
        Assert.Equal(1, await new OutboxClaimStore(b).CompleteAsync(fresh, later, CancellationToken.None));
    }
    [PgFact]
    public async Task EnqueueRollsBackWithBusinessTransaction()
    {
        await using var rig = new SyncPgRig(pg.ConnectionString); await rig.Initialize(); await using var db = rig.Factory.Create();
        var position = await new FeedPositionReader(db).GetAsync(rig.Tenant);
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Tenants\" SET \"DisplayName\"='changed' WHERE \"Id\"={rig.Tenant}");
            var operation = IdGenerator.NewId(DateTimeOffset.UtcNow);
            var envelope = CommandEnvelope.Create(operation, rig.Tenant, operation, operation, "test.update", "Tenant", rig.Tenant, "{}", "test", DateTimeOffset.UtcNow, 1);
            var settled = new SettlementResult(operation, SettlementOutcome.Accepted, null, null, rig.Tenant, null,
                DateTimeOffset.UtcNow, FinancialEffect.None, null);
            Assert.True((await new ServerOutbox(db).AppendAsync(envelope, settled)).IsSuccess);
            Assert.Single(db.ChangeTracker.Entries<OutboxMessage>());
            await db.SaveChangesAsync(); await tx.RollbackAsync();
        }
        db.ChangeTracker.Clear(); Assert.Empty(await db.Set<OutboxMessage>().ToArrayAsync());
        Assert.Equal(position.LastSequence, (await new FeedPositionReader(db).GetAsync(rig.Tenant)).LastSequence);
    }
    [PgFact]
    public async Task Redelivery_ConsumerEffectRunsOnce()
    {
        await using var rig = new SyncPgRig(pg.ConnectionString); await rig.Initialize(); await Seed(rig, 1);
        await using var db = Context(rig); var now = DateTimeOffset.UtcNow;
        var claim = Assert.Single(await new OutboxClaimStore(db).ClaimAsync(now, 1, CancellationToken.None));
        var consumer = new CountConsumer(); var runner = new OutboxConsumerRunner(db);
        Assert.True((await runner.RunAsync(consumer, claim, now, CancellationToken.None)).IsSuccess);
        Assert.True((await runner.RunAsync(consumer, claim, now, CancellationToken.None)).IsSuccess);
        Assert.Equal(1, consumer.Count); Assert.Single(await db.Consumed.ToArrayAsync());
    }
    private sealed class CountConsumer : IOutboxConsumer
    {
        public string MessageType => "test.message"; public string Name => "test-consumer"; public int Count;
        public Task<Result> ConsumeAsync(OutboxMessage message, CancellationToken ct) { Count++; return Task.FromResult(Result.Ok()); }
    }
}
public sealed class WorkerRecoveryTests(PgFixture pg) : IClassFixture<PgFixture>
{
    [PgFact]
    public async Task KilledWorker_LeaseExpires_RowProcessed()
    {
        await using var rig = new SyncPgRig(pg.ConnectionString); await rig.Initialize(); await OutboxClaimTests.Seed(rig, 1);
        var now = DateTimeOffset.UtcNow;
        await using (var killed = OutboxClaimTests.Context(rig))
            Assert.Single(await new OutboxClaimStore(killed).ClaimAsync(now, 1, CancellationToken.None));
        await using var replacement = OutboxClaimTests.Context(rig);
        var later = now + OutboxClaimStore.Lease + TimeSpan.FromSeconds(1);
        var message = Assert.Single(await new OutboxClaimStore(replacement).ClaimAsync(later, 1, CancellationToken.None));
        Assert.Equal(2, message.Attempts);
        Assert.Equal(1, await new OutboxClaimStore(replacement).CompleteAsync(message, later, CancellationToken.None));
    }
}
