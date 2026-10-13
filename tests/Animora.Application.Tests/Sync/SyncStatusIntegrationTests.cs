using Animora.Application.Commands;
using Animora.Client.Services.Sync;
using Animora.Contracts.Sync;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Persistence.Tests;
using Animora.Sync.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests.Sync;

public sealed class SyncStatusIntegrationTests
{
    private sealed class IdleTransport : ICommandTransport
    {
        public Task<Result<SettlementResult>> SubmitAsync(CommandEnvelope envelope, CancellationToken ct = default) =>
            Task.FromResult(Result<SettlementResult>.Fail(ErrorCodes.NetworkUnavailable));
    }

    private static async Task<(LocalStoreTestFixture Fixture, LocalSyncStatusService Status, FakeStateSyncEngine Engine)> ArrangeAsync(
        bool engineConfigured = true, ICommandTransport? transport = null)
    {
        var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        var engine = new FakeStateSyncEngine { IsConfigured = engineConfigured };
        var scopes = fixture.Services.GetRequiredService<IServiceScopeFactory>();
        var orchestrator = new SyncOrchestrator(engine, scopes, new SyncScheduleOptions(), new RecordingResyncHandler());
        var status = new LocalSyncStatusService(scopes, transport ?? new IdleTransport(), fixture.Clock, engine, orchestrator);
        return (fixture, status, engine);
    }

    [Fact]
    public async Task Applied_NoBacklog_MapsSynced()
    {
        var (fixture, status, engine) = await ArrangeAsync();
        using (fixture) using (status)
        {
            engine.Then(FakeStateSyncEngine.Ok(SyncOutcome.Applied, fixture.Clock.Now));
            await status.SyncNowAsync();
            Assert.Equal(SyncState.Synced, status.State);
            Assert.Equal(fixture.Clock.Now, status.LastSyncedAt);
        }
    }

    [Fact]
    public async Task PendingCommands_MapToPending()
    {
        var (fixture, status, engine) = await ArrangeAsync();
        using (fixture) using (status)
        {
            using (var scope = fixture.CreateScope())
            {
                var journal = scope.ServiceProvider.GetRequiredService<ICommandJournal>();
                var envelope = CommandEnvelope.Create(IdGenerator.NewId(), LocalStoreTestFixture.DefaultTenant, "U1", "D1",
                    "sales.invoice.post", "Invoice", "INV-1", "{}", "fp", DateTimeOffset.UnixEpoch, 1);
                var uow = scope.ServiceProvider.GetRequiredService<Animora.Application.Abstractions.IUnitOfWork>();
                var enqueued = await uow.ExecuteAsync(async ct => await journal.EnqueueAsync(envelope, ct));
                Assert.True(enqueued.IsSuccess);
            }
            engine.Then(FakeStateSyncEngine.Ok(SyncOutcome.UpToDate, fixture.Clock.Now));
            await status.RefreshAsync();
            Assert.Equal(SyncState.Pending, status.State);
        }
    }

    [Fact]
    public async Task Unconfigured_MapsOffline()
    {
        var (fixture, status, engine) = await ArrangeAsync(engineConfigured: false, transport: new NullCommandTransport());
        using (fixture) using (status)
        {
            Assert.Equal(SyncState.Offline, status.State);
            await status.SyncNowAsync();
            Assert.Equal(SyncState.Offline, status.State);
            Assert.Equal(0, engine.Pulls);
        }
    }

    [Fact]
    public async Task Fail_MapsError()
    {
        var (fixture, status, engine) = await ArrangeAsync();
        using (fixture) using (status)
        {
            engine.Then(Result<SyncResult>.Fail(ErrorCodes.NetworkUnavailable));
            await status.SyncNowAsync();
            Assert.Equal(SyncState.Error, status.State);
        }
    }

    [Fact]
    public async Task ResyncRequired_MapsError()
    {
        var (fixture, status, engine) = await ArrangeAsync();
        using (fixture) using (status)
        {
            engine.Then(FakeStateSyncEngine.Ok(SyncOutcome.ResyncRequired, fixture.Clock.Now));
            await status.SyncNowAsync();
            Assert.Equal(SyncState.Error, status.State);
        }
    }
    [Fact]
    public async Task TenantIsolation_DeferredPullNeverAdvancesCombinedStatusCursor()
    {
        var (fixture, status, engine) = await ArrangeAsync();
        using (fixture) using (status)
        {
            engine.Then(FakeStateSyncEngine.Ok(SyncOutcome.Deferred, fixture.Clock.Now));
            await status.SyncNowAsync();
            Assert.Equal(SyncState.Offline, status.State);
            Assert.Null(status.LastSyncedAt);
        }
    }

    [Fact]
    public async Task TenantIsolation_MismatchBlocksClaimBeforeAnyRetryOrPull()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        var binding = new Animora.Infrastructure.Local.Storage.TenantSessionBinding(new MemorySecretStore());
        binding.Bind("A");
        binding.Activate();
        using (var scope = fixture.CreateScope())
        {
            var journal = scope.ServiceProvider.GetRequiredService<ICommandJournal>();
            var envelope = CommandEnvelope.Create("UNSENT-A", LocalStoreTestFixture.DefaultTenant, "U", "D", "clinic.pet.update", "Pet", "P", "{}", "fp", fixture.Clock.Now, 1);
            await journal.EnqueueAsync(envelope);
            await scope.ServiceProvider.GetRequiredService<Animora.Persistence.Sqlite.Context.AnimoraLocalDbContext>().SaveChangesAsync();
        }
        var scopes = fixture.Services.GetRequiredService<IServiceScopeFactory>();
        var engine = new FakeStateSyncEngine();
        var sessions = new MismatchedSessions();
        var orchestrator = new SyncOrchestrator(engine, scopes, new SyncScheduleOptions(), new RecordingResyncHandler(), sessions, binding);
        using var status = new LocalSyncStatusService(scopes, new IdleTransport(), fixture.Clock, engine, orchestrator, sessions, binding);
        await status.SyncNowAsync();
        Assert.Null(status.LastSyncedAt);
        Assert.Equal(0, engine.Pulls);
        Assert.Equal(1, status.PendingChangeCount);
        using var read = fixture.CreateScope();
        var row = (await read.ServiceProvider.GetRequiredService<ICommandJournal>().FindByOperationIdAsync("UNSENT-A")).Value!;
        Assert.Equal(0, row.Attempts);
        Assert.Null(row.LastAttemptUtc);
    }

    private sealed class MismatchedSessions : Animora.Application.Security.ISessionStore
    {
        public Task<Result<Animora.Application.Security.ClientSession?>> ReadAsync(CancellationToken ct = default) => Task.FromResult(
            Result<Animora.Application.Security.ClientSession?>.Ok(new("access", DateTimeOffset.MaxValue, "refresh", "U", "B", "D", "Manager", "s")));
        public Task<Result> WriteAsync(Animora.Application.Security.ClientSession session, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result> ClearAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }
}
