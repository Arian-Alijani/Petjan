using Animora.Client.Services.Sync;
using Animora.Domain.Results;
using Animora.Persistence.Tests;
using Animora.Sync.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests.Sync;

public sealed class SyncSchedulerTests
{
    private static SyncOrchestrator Orchestrator(FakeStateSyncEngine engine, LocalStoreTestFixture fixture, SyncScheduleOptions options) =>
        new(engine, fixture.Services.GetRequiredService<IServiceScopeFactory>(), options, new RecordingResyncHandler());

    private static async Task<LocalStoreTestFixture> FixtureAsync()
    {
        var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        return fixture;
    }

    [Fact]
    public async Task Dormant_WhenUnconfigured()
    {
        using var fixture = await FixtureAsync();
        var engine = new FakeStateSyncEngine { IsConfigured = false };
        var options = new SyncScheduleOptions();
        var delay = new FakeDelayProvider();
        await using var scheduler = new SyncSchedulerHostedService(engine, Orchestrator(engine, fixture, options), options, delay);

        await scheduler.ExecuteAsync(CancellationToken.None);
        scheduler.Start();

        Assert.Equal(0, engine.Pulls);
        Assert.Empty(delay.Delays);
    }

    [Fact]
    public async Task RunsOnInterval_WithJitter()
    {
        using var fixture = await FixtureAsync();
        using var stop = new CancellationTokenSource();
        var engine = new FakeStateSyncEngine().Then(FakeStateSyncEngine.Ok(SyncOutcome.UpToDate, fixture.Clock.Now));
        var options = new SyncScheduleOptions { Interval = TimeSpan.FromMinutes(5), JitterFraction = 0.2 };
        var delay = new FakeDelayProvider(stop, stopAfter: 6);
        await using var scheduler = new SyncSchedulerHostedService(engine, Orchestrator(engine, fixture, options), options, delay);

        await scheduler.ExecuteAsync(stop.Token);

        Assert.Equal(6, engine.Pulls);
        Assert.All(delay.Delays, d => Assert.InRange(d, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(6)));
        Assert.True(delay.Delays.Distinct().Count() > 1, "jitter must vary the wait");
    }

    [Fact]
    public async Task Backoff_DoublesOnFault_CapsAtMax_ResetsOnSuccess()
    {
        using var fixture = await FixtureAsync();
        using var stop = new CancellationTokenSource();
        var fail = Result<SyncResult>.Fail(ErrorCodes.NetworkUnavailable);
        var engine = new FakeStateSyncEngine()
            .Then(fail).Then(fail).Then(fail).Then(fail)
            .Then(FakeStateSyncEngine.Ok(SyncOutcome.UpToDate, fixture.Clock.Now));
        var options = new SyncScheduleOptions
        {
            Interval = TimeSpan.FromMinutes(1),
            MinInterval = TimeSpan.FromSeconds(30),
            MaxBackoff = TimeSpan.FromMinutes(5),
            JitterFraction = 0,
        };
        var delay = new FakeDelayProvider(stop, stopAfter: 5);
        await using var scheduler = new SyncSchedulerHostedService(engine, Orchestrator(engine, fixture, options), options, delay);

        await scheduler.ExecuteAsync(stop.Token);

        Assert.Equal(
            [TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1)],
            delay.Delays);
    }

    [Fact]
    public async Task CancellationExitsCleanly()
    {
        using var fixture = await FixtureAsync();
        using var stop = new CancellationTokenSource();
        var engine = new FakeStateSyncEngine().Then(FakeStateSyncEngine.Ok(SyncOutcome.UpToDate, fixture.Clock.Now));
        var options = new SyncScheduleOptions();
        var delay = new FakeDelayProvider(stop, stopAfter: 1);
        await using var scheduler = new SyncSchedulerHostedService(engine, Orchestrator(engine, fixture, options), options, delay);

        await scheduler.ExecuteAsync(stop.Token); // must not throw

        Assert.True(stop.IsCancellationRequested);
        Assert.Equal(1, engine.Pulls);
    }
    [Fact]
    public async Task TenantIsolation_SchedulerRetriesDurableOfflineJournalAndAutomaticallyDrainsAfterReconnect()
    {
        var transport = new ReconnectTransport();
        using var fixture = new LocalStoreTestFixture(configureServices: services => services.AddSingleton<Animora.Application.Commands.ICommandTransport>(transport));
        await fixture.MigrateAsync();
        using (var scope = fixture.CreateScope())
        {
            var journal = scope.ServiceProvider.GetRequiredService<Animora.Application.Commands.ICommandJournal>();
            var envelope = Animora.Contracts.Wire.Commands.CommandEnvelope.Create("OFFLINE-1", LocalStoreTestFixture.DefaultTenant,
                "U", "D", "clinic.pet.update", "Pet", "P", "{}", "fp", fixture.Clock.Now, 1);
            await journal.EnqueueAsync(envelope);
            await scope.ServiceProvider.GetRequiredService<Animora.Persistence.Sqlite.Context.AnimoraLocalDbContext>().SaveChangesAsync();
        }
        var engine = new FakeStateSyncEngine().Then(FakeStateSyncEngine.Ok(SyncOutcome.Deferred, fixture.Clock.Now));
        var options = new SyncScheduleOptions();
        var orchestrator = Orchestrator(engine, fixture, options);
        using var status = new LocalSyncStatusService(fixture.Services.GetRequiredService<IServiceScopeFactory>(), transport, fixture.Clock, engine, orchestrator);
        using (var offlineStop = new CancellationTokenSource())
        {
            await using var offline = new SyncSchedulerHostedService(engine, orchestrator, options, new FakeDelayProvider(offlineStop, 1), status);
            await offline.ExecuteAsync(offlineStop.Token);
        }
        Assert.Equal(1, status.PendingChangeCount);
        Assert.Null(status.LastSyncedAt);
        using (var scope = fixture.CreateScope())
        {
            var row = (await scope.ServiceProvider.GetRequiredService<Animora.Application.Commands.ICommandJournal>().FindByOperationIdAsync("OFFLINE-1")).Value!;
            Assert.Equal(0, row.Attempts); // offline deferral must not consume retry budget
        }
        fixture.Clock.Now = fixture.Clock.Now.AddHours(1); // deterministic retry due-time, no real wait
        transport.Online = true;
        engine.Then(FakeStateSyncEngine.Ok(SyncOutcome.UpToDate, fixture.Clock.Now));
        using (var onlineStop = new CancellationTokenSource())
        {
            await using var online = new SyncSchedulerHostedService(engine, orchestrator, options, new FakeDelayProvider(onlineStop, 1), status);
            await online.ExecuteAsync(onlineStop.Token);
        }
        Assert.Equal(0, status.PendingChangeCount);
        Assert.Equal(Animora.Contracts.Sync.SyncState.Synced, status.State);
        Assert.Equal(fixture.Clock.Now, status.LastSyncedAt);
        Assert.Equal(2, transport.Sent.Count);
        Assert.Equal(transport.Sent[0], transport.Sent[1]); // same operation, fingerprint and payload
    }

    private sealed class ReconnectTransport : Animora.Application.Commands.ICommandTransport
    {
        public bool Online { get; set; }
        public List<Animora.Contracts.Wire.Commands.CommandEnvelope> Sent { get; } = [];
        public Task<Result<Animora.Contracts.Wire.Commands.SettlementResult>> SubmitAsync(Animora.Contracts.Wire.Commands.CommandEnvelope envelope, CancellationToken ct = default)
        {
            Sent.Add(envelope);
            return Task.FromResult(Result<Animora.Contracts.Wire.Commands.SettlementResult>.Ok(new(envelope.OperationId,
                Online ? Animora.Contracts.Wire.Commands.SettlementOutcome.Accepted : Animora.Contracts.Wire.Commands.SettlementOutcome.Deferred,
                null, null, null, null, envelope.CreatedAtUtc, Animora.Contracts.Wire.Commands.FinancialEffect.None, null)));
        }
    }
}
