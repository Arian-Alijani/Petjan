namespace Animora.Contracts.Diagnostics;

/// <summary>Signals when the local store has finished the 8-step startup so a ViewModel's first DB read
/// never races LocalStoreStartup (C01). Awaiting is a no-op once the store is ready.</summary>
public interface IStoreReadinessGate
{
    /// <summary>Completes when the store is ready; returns immediately if it already is.</summary>
    Task WaitAsync(CancellationToken ct = default);

    /// <summary>Marks the store ready, releasing every awaiter. Idempotent.</summary>
    void MarkReady();
}
