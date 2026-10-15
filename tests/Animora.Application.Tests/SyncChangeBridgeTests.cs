using System;
using System.Collections.Generic;
using Animora.Contracts.Sync;
using Animora.Desktop.Services;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>
/// D14: a background sync pass writes straight into the local store, so the page the operator is looking at must be
/// told — the local write path announces its changes through <see cref="IDataChangeNotifier"/>, the sync path never
/// did. <see cref="SyncChangeBridge"/> re-announces a completed pass on that same channel, exactly once per pass, and
/// only when the pass actually advanced the sync cursor.
/// </summary>
public sealed class SyncChangeBridgeTests
{
    private sealed class FakeSyncStatus : ISyncStatusService
    {
        public SyncState State { get; set; } = SyncState.Idle;
        public DateTimeOffset? LastSyncedAt { get; set; }
        public int PendingChangeCount => 0;
        public event EventHandler? StateChanged;
        public System.Threading.Tasks.Task SyncNowAsync(System.Threading.CancellationToken ct = default) =>
            System.Threading.Tasks.Task.CompletedTask;

        /// <summary>Simulates the status service publishing a new snapshot (as the real one does each pass).</summary>
        public void Publish(SyncState state, DateTimeOffset? lastSyncedAt)
        {
            State = state;
            LastSyncedAt = lastSyncedAt;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class RecordingNotifier : IDataChangeNotifier
    {
        public List<DataDomain[]> Announcements { get; } = [];
        public void Notify(params DataDomain[] domains) => Announcements.Add(domains);
        public IDisposable Subscribe(Action<DataDomain> handler) => new Noop();
        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }

    private static readonly DateTimeOffset Pass1 = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CompletedPass_AnnouncesABusinessDataChange()
    {
        var sync = new FakeSyncStatus();
        var changes = new RecordingNotifier();
        using var bridge = new SyncChangeBridge(sync, changes);
        bridge.Start();

        sync.Publish(SyncState.Syncing, null);              // pass starts — nothing to announce yet
        sync.Publish(SyncState.Synced, Pass1);              // pass completed and advanced the cursor

        var announcement = Assert.Single(changes.Announcements);
        Assert.Contains(DataDomain.Sales, announcement);
        Assert.Contains(DataDomain.Patients, announcement);
        Assert.Contains(DataDomain.History, announcement);
    }

    [Fact]
    public void OnePass_AnnouncesOnce_EvenWithSeveralStateChanges()
    {
        var sync = new FakeSyncStatus();
        var changes = new RecordingNotifier();
        using var bridge = new SyncChangeBridge(sync, changes);
        bridge.Start();

        // A single real pass publishes Syncing, then the completed snapshot; only the latter advances the cursor.
        sync.Publish(SyncState.Syncing, null);
        sync.Publish(SyncState.Synced, Pass1);
        sync.Publish(SyncState.Pending, Pass1);             // queued work remains, same completed pass

        Assert.Single(changes.Announcements);
    }

    [Fact]
    public void QueuedWorkLeftBehind_StillAnnouncesTheAppliedPull()
    {
        var sync = new FakeSyncStatus();
        var changes = new RecordingNotifier();
        using var bridge = new SyncChangeBridge(sync, changes);
        bridge.Start();

        // The reported state may be Pending while the pull still applied rows — the cursor is the reliable trigger.
        sync.Publish(SyncState.Pending, Pass1);

        Assert.Single(changes.Announcements);
    }

    [Fact]
    public void FailedPass_ThatNeverAdvancesTheCursor_AnnouncesNothing()
    {
        var sync = new FakeSyncStatus();
        var changes = new RecordingNotifier();
        using var bridge = new SyncChangeBridge(sync, changes);
        bridge.Start();

        sync.Publish(SyncState.Syncing, null);
        sync.Publish(SyncState.Error, null);
        sync.Publish(SyncState.Offline, null);

        Assert.Empty(changes.Announcements);
    }

    [Fact]
    public void SecondPass_AnnouncesAgain()
    {
        var sync = new FakeSyncStatus();
        var changes = new RecordingNotifier();
        using var bridge = new SyncChangeBridge(sync, changes);
        bridge.Start();

        sync.Publish(SyncState.Synced, Pass1);
        sync.Publish(SyncState.Syncing, Pass1);
        sync.Publish(SyncState.Synced, Pass1.AddMinutes(5));

        Assert.Equal(2, changes.Announcements.Count);
    }

    [Fact]
    public void AfterDispose_NoFurtherAnnouncements()
    {
        var sync = new FakeSyncStatus();
        var changes = new RecordingNotifier();
        var bridge = new SyncChangeBridge(sync, changes);
        bridge.Start();
        bridge.Dispose();

        sync.Publish(SyncState.Synced, Pass1);

        Assert.Empty(changes.Announcements);
    }
}
