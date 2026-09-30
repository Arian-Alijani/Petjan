using Animora.Contracts.Sync;

namespace Animora.Desktop.Mocks;

/// <summary>
/// In-memory <see cref="ISyncStatusService"/> for the topbar's sync box. Starts
/// in the healthy <see cref="SyncState.Synced"/> state with a fresh timestamp;
/// <see cref="SyncNowAsync"/> simulates a real sync pass (spin for ~1.2s, then
/// settle on <see cref="SyncState.Synced"/> and bump <see cref="LastSyncedAt"/>),
/// raising <see cref="StateChanged"/> on every transition so the UI updates.
/// This is the mock stand-in for the future <c>ISyncEngine</c>-backed service.
/// </summary>
public sealed class MockSyncStatusService : ISyncStatusService
{
    // A fixed, deterministic "sync takes 1.2s" delay, matching the task spec.
    private static readonly TimeSpan SyncDuration = TimeSpan.FromMilliseconds(1200);

    public MockSyncStatusService()
    {
        State = SyncState.Synced;
        LastSyncedAt = DateTimeOffset.Now;
    }

    public SyncState State { get; private set; }

    public DateTimeOffset? LastSyncedAt { get; private set; }

    public event EventHandler? StateChanged;

    public async Task SyncNowAsync(CancellationToken ct = default)
    {
        SetState(SyncState.Syncing);

        try
        {
            await Task.Delay(SyncDuration, ct);
        }
        catch (OperationCanceledException)
        {
            // A cancelled pass just returns to the last settled state.
            SetState(SyncState.Synced);
            return;
        }

        LastSyncedAt = DateTimeOffset.Now;
        SetState(SyncState.Synced);
    }

    private void SetState(SyncState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
