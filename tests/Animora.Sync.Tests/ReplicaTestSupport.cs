using System.Text.Json;
using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Contracts.Wire.Commands;
using Animora.Contracts.Wire.Sync;
using Animora.Domain.Primitives;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Replica;
using Animora.Persistence.Tests;
using Animora.Sync.Abstractions;
using Animora.Sync.Custom;
using Animora.Sync.Custom.Replica;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Sync.Tests;

/// <summary>Helpers for the G04 replica-store tests: a migrated file database, batch/page builders and replica readers.</summary>
internal static class ReplicaTestSupport
{
    public const long Epoch = 7;

    public static async Task<LocalStoreTestFixture> NewFixtureAsync()
    {
        var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        return fixture;
    }

    public static SqliteSyncApplyStore NewStore(LocalStoreTestFixture fixture) =>
        new(fixture.Services.GetRequiredService<IServiceScopeFactory>());

    public static SyncRecord Rec(string entity, string id, string version, string name, char op = 'U') =>
        new(entity, id, op, version, FeedFixtures.Now, op == 'D',
            op == 'D' ? null : JsonDocument.Parse($"{{\"name\":\"{name}\"}}").RootElement.Clone());

    public static SyncPage Page(string batchId, int ordinal, int pageCount, params SyncRecord[] records) =>
        new(batchId, ordinal, pageCount, records, "");

    public static BatchReady Batch(string id, int pageCount, long from, long watermark, long generation = 1) =>
        new(id, Epoch, generation, from, watermark, pageCount, "hash");

    /// <summary>Opens a session, streams <paramref name="pages"/> and commits.</summary>
    public static async Task<Animora.Domain.Results.Result<SyncCheckpoint>> ApplyAsync(
        SqliteSyncApplyStore store, BatchReady batch, params SyncPage[] pages)
    {
        var begun = await store.BeginAsync(SyncScope.Default, batch);
        Assert.True(begun.IsSuccess, begun.Detail);
        await using var session = begun.Value!;
        foreach (var page in pages)
        {
            var accepted = await session.AcceptPageAsync(page);
            Assert.True(accepted.IsSuccess, accepted.Detail);
        }

        return await session.CommitAsync();
    }

    public static async Task<(long Epoch, long Watermark, long Generation)> CheckpointAsync(LocalStoreTestFixture fixture)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var row = await db.Set<ReplicaCheckpoint>().SingleOrDefaultAsync(x => x.TenantId == fixture.TenantId);
        return row is null ? (0, 0, 0) : (row.Epoch, row.Watermark, row.Generation);
    }

    public static async Task<List<ReplicaBase>> BaseRowsAsync(LocalStoreTestFixture fixture)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        return await db.Set<ReplicaBase>().Where(x => x.TenantId == fixture.TenantId)
            .OrderBy(x => x.Generation).ThenBy(x => x.EntityId).ToListAsync();
    }

    public static async Task<int> StagingCountAsync(LocalStoreTestFixture fixture)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        return await db.Set<ReplicaStaging>().CountAsync();
    }

    /// <summary>Enqueues one pending command (the overlay) and returns its operation id.</summary>
    public static async Task<string> EnqueueOverlayAsync(LocalStoreTestFixture fixture)
    {
        using var scope = fixture.CreateScope();
        var journal = scope.ServiceProvider.GetRequiredService<ICommandJournal>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var operationId = IdGenerator.NewId();
        var envelope = CommandEnvelope.Create(operationId, fixture.TenantId, "U1", "D1", "sales.invoice.post", "Invoice",
            "INV-1", "{}", "fp", DateTimeOffset.UnixEpoch, 1);
        var enqueued = await uow.ExecuteAsync(async ct => await journal.EnqueueAsync(envelope, ct));
        Assert.True(enqueued.IsSuccess);
        return operationId;
    }

    public static async Task<List<(string Id, int State)>> OverlayAsync(LocalStoreTestFixture fixture)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var rows = await db.Set<PendingCommand>().OrderBy(x => x.OperationId).ToListAsync();
        return rows.Select(x => (x.OperationId, (int)x.Status)).ToList();
    }
}
