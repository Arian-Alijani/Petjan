using System;
using Animora.Contracts.Sync;
using Animora.Desktop.Diagnostics;

namespace Animora.Desktop.Services;

/// <summary>
/// D14: a background sync pass writes rows straight into the local store, so every open page goes stale the moment
/// the pass finishes — the local write path announces its changes through <see cref="IDataChangeNotifier"/>, but the
/// sync path never did, so a page only picked the new data up after the operator navigated away and back.
/// <para>
/// This bridge closes that gap from the UI side, which is where it belongs: sync lives in <c>Animora.Client.Services</c>
/// and must not know about pages or about this notifier. It observes the sync status the shell already publishes and,
/// when a pass actually COMPLETED, re-announces a coarse "business data changed" signal on the same channel the local
/// path uses. <see cref="Shell.PageViewModelBase.RefreshOnDataChange"/> filters by domain and only reloads the page the
/// operator is actually looking at, so the coarse signal costs nothing on hidden pages.
/// </para>
/// <para>
/// <b>One announcement per completed pass.</b> The trigger is <see cref="ISyncStatusService.LastSyncedAt"/>
/// advancing, not the reported state: the status service advances that cursor only after a pass whose command batch
/// and state pull were both healthy, while the reported state can legitimately be <see cref="SyncState.Pending"/>
/// (queued work remains) or <see cref="SyncState.NeedsAttention"/> even though rows were just applied. Using the
/// cursor also coalesces the burst of state changes one pass produces into a single announcement — no timer needed —
/// and guarantees a failed, deferred or offline pass (which never advances the cursor) announces nothing.
/// </para>
/// </summary>
public sealed class SyncChangeBridge : IDisposable
{
    /// <summary>
    /// Every business domain a pass may have touched. A pull is not scoped to one screen — the same pass can carry a
    /// peer's patient, visit, invoice and stock rows — so the announcement is deliberately coarse; the per-page domain
    /// filter is what keeps it cheap.
    /// </summary>
    private static readonly DataDomain[] BusinessDomains =
    [
        DataDomain.Patients, DataDomain.Visits, DataDomain.Vaccination, DataDomain.Appointments,
        DataDomain.Boarding, DataDomain.Inventory, DataDomain.Purchasing, DataDomain.Sales,
        DataDomain.Cashier, DataDomain.Cheques, DataDomain.Finance, DataDomain.History, DataDomain.Dashboard,
    ];

    private readonly ISyncStatusService _sync;
    private readonly IDataChangeNotifier _changes;
    private readonly object _gate = new();
    private DateTimeOffset? _announcedFor;
    private bool _started;

    public SyncChangeBridge(ISyncStatusService sync, IDataChangeNotifier changes)
    {
        _sync = sync;
        _changes = changes;
    }

    /// <summary>Begins observing sync completions. Idempotent.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _sync.StateChanged += OnSyncStateChanged;
        AppTrace.Info("SyncChangeBridge started — a completed sync pass will refresh the open page (D14).", "sync.apply");
    }

    public void Dispose()
    {
        if (!_started) return;
        _started = false;
        _sync.StateChanged -= OnSyncStateChanged;
    }

    private void OnSyncStateChanged(object? sender, EventArgs e)
    {
        // Raised from a background thread by the sync pass. No marshalling is needed here: IDataChangeNotifier's
        // contract is that subscribers are always invoked on the UI thread and that a Notify from another thread is
        // marshalled for them, so the bridge stays free of any dispatcher dependency.
        //
        // The cursor only advances after a healthy pass, so "it moved" is exactly "this pass may have applied rows".
        if (_sync.LastSyncedAt is not { } completedAt) return;

        lock (_gate)
        {
            if (_announcedFor == completedAt) return; // already announced for this completed pass
            _announcedFor = completedAt;
        }

        AppTrace.Info($"SyncChangeBridge — pass completed at {completedAt:O} (state={_sync.State}); announcing a business-data change.", "sync.apply");
        _changes.Notify(BusinessDomains);
    }
}
