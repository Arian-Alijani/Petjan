namespace Animora.Sync.Abstractions;

/// <summary>The classification of a completed pull; genuine faults are a Result.Fail, never an outcome.</summary>
public enum SyncOutcome
{
    /// <summary>The local base already reflects the server watermark; nothing downloaded.</summary>
    UpToDate = 0,

    /// <summary>One or more immutable batches were downloaded, verified and applied.</summary>
    Applied = 1,

    /// <summary>No engine/endpoint configured (offline); the NullSyncEngine answer — a success.</summary>
    Deferred = 2,

    /// <summary>The server reported the client checkpoint is unusable; the orchestrator escalates to full resync.</summary>
    ResyncRequired = 3,
}
