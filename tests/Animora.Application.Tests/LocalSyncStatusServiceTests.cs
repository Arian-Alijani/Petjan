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
        Assert.Equal(fixture.Clock.Now, service.LastSyncedAt);
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

    private sealed class UnexpectedTransport : ICommandTransport
    {
        public Task<Result<SettlementResult>> SubmitAsync(CommandEnvelope envelope, CancellationToken ct = default) =>
            throw new InvalidOperationException("An empty journal must not submit anything.");
    }
}
