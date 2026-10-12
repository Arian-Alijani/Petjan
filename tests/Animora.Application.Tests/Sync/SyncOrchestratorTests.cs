using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Client.Services.Sync;
using Animora.Domain.Results;
using Animora.Persistence.Tests;
using Animora.Sync.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests.Sync;

public sealed class SyncOrchestratorTests
{
    private static async Task<DateTimeOffset?> CursorAsync(LocalStoreTestFixture fixture)
    {
        using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISyncCursorStore>().ReadAsync();
    }

    private static async Task<(LocalStoreTestFixture Fixture, FakeStateSyncEngine Engine, RecordingResyncHandler Resync, SyncOrchestrator Orchestrator)> ArrangeAsync()
    {
        var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        var engine = new FakeStateSyncEngine();
        var resync = new RecordingResyncHandler();
        var orchestrator = new SyncOrchestrator(engine, fixture.Services.GetRequiredService<IServiceScopeFactory>(), new SyncScheduleOptions(), resync);
        return (fixture, engine, resync, orchestrator);
    }

    [Theory]
    [InlineData(SyncOutcome.Applied)]
    [InlineData(SyncOutcome.UpToDate)]
    public async Task PullSucceeded_AdvancesCursor(SyncOutcome outcome)
    {
        var (fixture, engine, _, orchestrator) = await ArrangeAsync();
        using (fixture)
        {
            var at = fixture.Clock.Now.AddMinutes(7);
            engine.Then(FakeStateSyncEngine.Ok(outcome, at));
            var result = await orchestrator.PullOnceAsync(SyncScope.Default);
            Assert.Equal(outcome, result.Value);
            Assert.Equal(at, await CursorAsync(fixture));
        }
    }

    private sealed class FakeSessionStore(ClientSession? session) : ISessionStore
    {
        public Task<Result<ClientSession?>> ReadAsync(CancellationToken ct = default) => Task.FromResult(Result<ClientSession?>.Ok(session));
        public Task<Result> WriteAsync(ClientSession s, CancellationToken ct = default) => Task.FromResult(Result.Ok());
        public Task<Result> ClearAsync(CancellationToken ct = default) => Task.FromResult(Result.Ok());
    }

    [Fact]
    public async Task NoOnlineSession_DefersWithoutTouchingEngine()
    {
        var (fixture, engine, _, _) = await ArrangeAsync();
        using (fixture)
        {
            var orchestrator = new SyncOrchestrator(engine, fixture.Services.GetRequiredService<IServiceScopeFactory>(),
                new SyncScheduleOptions(), new RecordingResyncHandler(), new FakeSessionStore(null));
            var result = await orchestrator.PullAllAsync();
            Assert.Equal(SyncOutcome.Deferred, result.Value);
            Assert.Equal(0, engine.Pulls);
            Assert.Null(await CursorAsync(fixture));
        }
    }

    [Fact]
    public async Task WithOnlineSession_PullsNormally()
    {
        var (fixture, engine, _, _) = await ArrangeAsync();
        using (fixture)
        {
            var session = new ClientSession("a", DateTimeOffset.UtcNow.AddHours(1), "r", "u", "t", "d", "Manager", "s");
            var orchestrator = new SyncOrchestrator(engine, fixture.Services.GetRequiredService<IServiceScopeFactory>(),
                new SyncScheduleOptions(), new RecordingResyncHandler(), new FakeSessionStore(session));
            engine.Then(FakeStateSyncEngine.Ok(SyncOutcome.UpToDate, fixture.Clock.Now));
            var result = await orchestrator.PullAllAsync();
            Assert.Equal(SyncOutcome.UpToDate, result.Value);
            Assert.Equal(1, engine.Pulls);
        }
    }

    [Fact]
    public async Task PullDeferred_DoesNotAdvanceCursor()
    {
        var (fixture, engine, _, orchestrator) = await ArrangeAsync();
        using (fixture)
        {
            engine.Then(FakeStateSyncEngine.Ok(SyncOutcome.Deferred, fixture.Clock.Now));
            var result = await orchestrator.PullOnceAsync(SyncScope.Default);
            Assert.Equal(SyncOutcome.Deferred, result.Value);
            Assert.Null(await CursorAsync(fixture));
        }
    }

    [Fact]
    public async Task ResyncRequired_InvokesHandler_NoCursorAdvance()
    {
        var (fixture, engine, resync, orchestrator) = await ArrangeAsync();
        using (fixture)
        {
            engine.Then(FakeStateSyncEngine.Ok(SyncOutcome.ResyncRequired, fixture.Clock.Now, "epoch-changed"));
            var result = await orchestrator.PullOnceAsync(SyncScope.Default);
            Assert.Equal(SyncOutcome.ResyncRequired, result.Value);
            var call = Assert.Single(resync.Calls);
            Assert.Equal("epoch-changed", call.Reason);
            Assert.Null(await CursorAsync(fixture));
        }
    }

    [Fact]
    public async Task PullFail_Surfaces_NoCursorAdvance()
    {
        var (fixture, engine, resync, orchestrator) = await ArrangeAsync();
        using (fixture)
        {
            engine.Then(Result<SyncResult>.Fail(ErrorCodes.NetworkUnavailable, "down"));
            var result = await orchestrator.PullOnceAsync(SyncScope.Default);
            Assert.True(result.IsFailure);
            Assert.Equal(ErrorCodes.NetworkUnavailable, result.Error);
            Assert.Empty(resync.Calls);
            Assert.Null(await CursorAsync(fixture));
        }
    }

    [Fact]
    public async Task PullAll_ReturnsWorstOutcome_AcrossScopes()
    {
        var (fixture, engine, _, _) = await ArrangeAsync();
        using (fixture)
        {
            var options = new SyncScheduleOptions { Scopes = ["default", "extra"] };
            var orchestrator = new SyncOrchestrator(engine, fixture.Services.GetRequiredService<IServiceScopeFactory>(), options, new RecordingResyncHandler());
            engine.Then(FakeStateSyncEngine.Ok(SyncOutcome.Applied, fixture.Clock.Now))
                  .Then(FakeStateSyncEngine.Ok(SyncOutcome.Deferred, fixture.Clock.Now));
            var result = await orchestrator.PullAllAsync();
            Assert.Equal(SyncOutcome.Deferred, result.Value);
            Assert.Equal(2, engine.Pulls);
        }
    }
}
