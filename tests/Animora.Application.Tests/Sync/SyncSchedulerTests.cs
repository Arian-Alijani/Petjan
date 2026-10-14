using Animora.Client.Services.Sync;
using Animora.Application.Commands;
using Animora.Application.Abstractions;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Primitives;
using Animora.Contracts.Sync;
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
    private sealed class AcceptingTransport : ICommandTransport
    {
        public int Submitted { get; private set; }
        public Task<Result<SettlementResult>> SubmitAsync(CommandEnvelope envelope, CancellationToken ct = default)
        {
            Submitted++;
            return Task.FromResult(Result<SettlementResult>.Ok(new SettlementResult(envelope.OperationId,
                SettlementOutcome.Accepted, null, null, envelope.AggregateId, "v1", DateTimeOffset.UnixEpoch,
                FinancialEffect.None, null)));
        }
    }

    [Fact]
    public async Task ScheduledReconnect_FlushesDurableOfflineCommandsBeforePulling()
    {
        var transport = new AcceptingTransport();
        using var fixture = new LocalStoreTestFixture(configureServices: services => services.AddSingleton<ICommandTransport>(transport));
        await fixture.MigrateAsync();
        using (var scope = fixture.CreateScope())
        {
            var journal = scope.ServiceProvider.GetRequiredService<ICommandJournal>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var command = CommandEnvelope.Create(IdGenerator.NewId(), fixture.TenantId, "U1", "D1", "patients.owner.update",
                "Owner", "O1", "{}", "fp", DateTimeOffset.UnixEpoch, 1);
            Assert.True((await uow.ExecuteAsync(async ct => await journal.EnqueueAsync(command, ct))).IsSuccess);
        }
        var engine = new FakeStateSyncEngine().Then(FakeStateSyncEngine.Ok(SyncOutcome.UpToDate, fixture.Clock.Now));
        var options = new SyncScheduleOptions();
        var orchestrator = Orchestrator(engine, fixture, options);
        using var status = new LocalSyncStatusService(fixture.Services.GetRequiredService<IServiceScopeFactory>(), transport,
            fixture.Clock, engine, orchestrator);
        using var stop = new CancellationTokenSource();
        await using var scheduler = new SyncSchedulerHostedService(engine, orchestrator, options,
            new FakeDelayProvider(stop, stopAfter: 1), status);
        await scheduler.ExecuteAsync(stop.Token);
        Assert.Equal(1, transport.Submitted);
        Assert.Equal(1, engine.Pulls);
        Assert.Equal(0, status.PendingChangeCount);
        Assert.Equal(SyncState.Synced, status.State);
    }

}
