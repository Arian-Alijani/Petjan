namespace Animora.Contracts.Sync;

/// <summary>
/// Lifecycle of a sync pass, mirroring the states a real background sync engine
/// cycles through. <see cref="Syncing"/> is the only transient state; the rest
/// are settled states the UI can render directly.
/// </summary>
public enum SyncState
{
    /// <summary>No sync has run yet this session (initial/idle).</summary>
    Idle,

    /// <summary>A sync pass is in flight (the topbar spinner runs).</summary>
    Syncing,

    /// <summary>The last pass completed successfully (green dot).</summary>
    Synced,

    /// <summary>Local changes are queued, awaiting the next pass (amber dot).</summary>
    Pending,

    /// <summary>The last pass failed (red dot).</summary>
    Error,

    /// <summary>No connectivity — nothing can be sent right now (red dot).</summary>
    Offline,
}

/// <summary>
/// Read/trigger seam for the app's synchronization status, consumed by the
/// topbar's sync box. This is deliberately a thin presentation contract: the
/// topbar knows only about a state, a last-synced timestamp and a manual trigger
/// — never how sync actually works.
/// <para>
/// <b>Backend-ready seam.</b> A future real sync engine
/// (<c>Animora.Sync.Abstractions.IStateSyncEngine</c> plus its transport/store
/// implementation) will drive this interface: it publishes <see cref="State"/>
/// transitions through <see cref="StateChanged"/> and implements
/// <see cref="SyncNowAsync"/> as a real flush/pull round-trip. Because the view
/// binds only to this contract, swapping <c>MockSyncStatusService</c> for that
/// engine requires no view change at all.
/// </para>
/// </summary>
public interface ISyncStatusService
{
    /// <summary>The current sync state.</summary>
    SyncState State { get; }

    /// <summary>
    /// When the last successful sync completed, or <see langword="null"/> when no
    /// successful sync has happened yet.
    /// </summary>
    DateTimeOffset? LastSyncedAt { get; }

    /// <summary>
    /// Number of local changes still waiting to be flushed to the server (the unsettled command backlog).
    /// Zero means nothing is queued for upload. The topbar uses this to explain whether a manual sync has
    /// outbound work to do — especially useful after the app has been offline for a while.
    /// </summary>
    int PendingChangeCount { get; }

    /// <summary>Raised after <see cref="State"/> (and/or <see cref="LastSyncedAt"/>) changes.</summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Requests an immediate sync pass. Completes when the pass settles
    /// (success/failure) — the topbar awaits this only to know when to stop
    /// showing the spinner, never blocking the UI thread.
    /// </summary>
    Task SyncNowAsync(CancellationToken ct = default);
}
