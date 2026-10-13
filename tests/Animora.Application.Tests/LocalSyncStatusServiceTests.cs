using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Client.Services.Sync;
using Animora.Contracts.Sync;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Results;
using Animora.Persistence.Sqlite.Commands;
using Animora.Persistence.Tests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests;

public sealed class LocalSyncStatusServiceTests
{
    [Fact]
    public async Task OfflinePass_IsRealAndDurable_AndRefreshDoesNotAdvanceCursor()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        var scopes = fixture.Services.GetRequiredService<IServiceScopeFactory>();
        using var service = new LocalSyncStatusService(scopes, new NullCommandTransport(), fixture.Clock);
        var states = new List<SyncState>();
        service.StateChanged += (_, _) => states.Add(service.State);
        Assert.Equal(SyncState.Offline, service.State);
        Assert.Null(service.LastSyncedAt);
        await service.SyncNowAsync();
        Assert.Equal(new[] { SyncState.Syncing, SyncState.Offline }, states);
        Assert.Null(service.LastSyncedAt);
        fixture.Clock.Now = fixture.Clock.Now.AddHours(1);
        using var reopened = new LocalSyncStatusService(scopes, new NullCommandTransport(), fixture.Clock);
        await reopened.RefreshAsync();
        Assert.Equal(service.LastSyncedAt, reopened.LastSyncedAt);
        Assert.Equal(SyncState.Offline, reopened.State);
    }

    [Fact]
    public async Task ConfiguredEndpoint_WithNoPendingCommands_IsSynced()
    {
        var transport = new UnexpectedTransport();
        using var fixture = new LocalStoreTestFixture(configureServices: s => s.AddSingleton<ICommandTransport>(transport));
        await fixture.MigrateAsync();
        using var service = new LocalSyncStatusService(fixture.Services.GetRequiredService<IServiceScopeFactory>(), transport, fixture.Clock);
        await service.RefreshAsync();
        Assert.Equal(SyncState.Synced, service.State);
        await service.SyncNowAsync();
        Assert.Equal(SyncState.Synced, service.State);
        Assert.Equal(fixture.Clock.Now, service.LastSyncedAt);
    }

    [Fact]
    public async Task Cursor_IsTenantIsolated_AndNeverMovesBackwards()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var a = fixture.CreateContext("T-A");
        using var b = fixture.CreateContext("T-B");
        var first = new SyncCursorStore(a, new FixedTenantContext("T-A"));
        var second = new SyncCursorStore(b, new FixedTenantContext("T-B"));
        await first.MarkSyncedAsync(fixture.Clock.Now);
        Assert.Null(await second.ReadAsync());
        await first.MarkSyncedAsync(fixture.Clock.Now.AddDays(-1));
        Assert.Equal(fixture.Clock.Now, await first.ReadAsync());
    }

    [Fact]
    public async Task CancelledWait_DoesNotPublishSyncingOrChangeDurableState()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var service = new LocalSyncStatusService(fixture.Services.GetRequiredService<IServiceScopeFactory>(), new NullCommandTransport(), fixture.Clock);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SyncNowAsync(cancelled.Token));
        Assert.Equal(SyncState.Offline, service.State);
        Assert.Null(service.LastSyncedAt);
    }

    [Fact]
    public async Task TenantIsolation_DisposeCancelsAndDrainsManualPassWithoutLosingClaimedCommand()
    {
        var transport = new BlockingTransport();
        using var fixture = new LocalStoreTestFixture(configureServices: services => services.AddSingleton<ICommandTransport>(transport));
        await fixture.MigrateAsync();
        using (var scope = fixture.CreateScope())
        {
            var envelope = CommandEnvelope.Create("INFLIGHT-A", LocalStoreTestFixture.DefaultTenant, "U", "D", "clinic.pet.update", "Pet", "P", "{}", "fp", fixture.Clock.Now, 1);
            await scope.ServiceProvider.GetRequiredService<ICommandJournal>().EnqueueAsync(envelope);
            await scope.ServiceProvider.GetRequiredService<Animora.Persistence.Sqlite.Context.AnimoraLocalDbContext>().SaveChangesAsync();
        }
        var service = new LocalSyncStatusService(fixture.Services.GetRequiredService<IServiceScopeFactory>(), transport, fixture.Clock);
        var pass = service.SyncNowAsync();
        await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.DisposeAsync();
        await pass;
        service.Dispose(); // root DI aliases may dispose the same singleton again
        Assert.True(transport.Cancelled);
        using var read = fixture.CreateScope();
        Assert.NotNull((await read.ServiceProvider.GetRequiredService<ICommandJournal>().FindByOperationIdAsync("INFLIGHT-A")).Value);
        Assert.Null(service.LastSyncedAt);
    }

    private sealed class BlockingTransport : ICommandTransport
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled { get; private set; }
        public async Task<Result<SettlementResult>> SubmitAsync(CommandEnvelope envelope, CancellationToken ct = default)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
            throw new InvalidOperationException("A cancelled pass must not settle.");
        }
    }

    private sealed class UnexpectedTransport : ICommandTransport
    {
        public Task<Result<SettlementResult>> SubmitAsync(CommandEnvelope envelope, CancellationToken ct = default) =>
            throw new InvalidOperationException("An empty journal must not submit anything.");
    }
}
