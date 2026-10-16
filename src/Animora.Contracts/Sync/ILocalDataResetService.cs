namespace Animora.Contracts.Sync;

/// <summary>The outcome of a full local reset + re-sync request (<see cref="ILocalDataResetService.ResetAndResyncAsync"/>).</summary>
public enum LocalResetOutcome
{
    /// <summary>Local data was wiped and the full server snapshot was pulled back successfully — everything is in sync.</summary>
    ResyncedFromServer,

    /// <summary>Local data was wiped, but the server could not be reached to re-pull. The device will finish the full pull on the next online sync pass.</summary>
    ClearedPendingServer,

    /// <summary>Nothing was done because no clinic is bound yet (there is no server data to re-sync).</summary>
    NotBound,

    /// <summary>The reset failed before any destructive change was committed; the existing local data is intact.</summary>
    Failed,
}

/// <summary>
/// Result of a reset request. <see cref="Outcome"/> drives the UI message; <see cref="Detail"/> carries a diagnostic code
/// (never shown verbatim to the user) for logs.
/// </summary>
public readonly record struct LocalResetResult(LocalResetOutcome Outcome, string? Detail = null)
{
    /// <summary>True when the destructive wipe committed (whether or not the server was reachable for the re-pull).</summary>
    public bool Wiped => Outcome is LocalResetOutcome.ResyncedFromServer or LocalResetOutcome.ClearedPendingServer;
}

/// <summary>
/// Explicit, operator-confirmed "wipe everything local and pull it all back from the server" action exposed in Settings.
/// <para>
/// It destructively clears the entire local replica of the current clinic (all business data, the sync cursor and the
/// replica base/checkpoint — the installation identity survives), then pulls a complete fresh snapshot from the server so the
/// device ends up byte-for-byte consistent with server state. This is the recovery path for a device whose local store is
/// suspect or out of step with the server; it is NOT a per-record undo. The two steps run under the sync transition gate so no
/// background pull races the wipe.
/// </para>
/// </summary>
public interface ILocalDataResetService
{
    /// <summary>
    /// Wipes all local data for the current clinic and re-pulls the full server snapshot. Completes only when the wipe (and, when
    /// the server is reachable, the re-pull) has settled. Honours cancellation BEFORE the destructive step; once the wipe has
    /// committed the operation runs to a safe settled state.
    /// </summary>
    Task<LocalResetResult> ResetAndResyncAsync(CancellationToken ct = default);
}
