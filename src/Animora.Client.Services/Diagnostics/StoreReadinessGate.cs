using Animora.Contracts.Diagnostics;

namespace Animora.Client.Services.Diagnostics;

/// <summary>Singleton readiness gate. App startup calls MarkReady() once LocalStoreStartup succeeds.</summary>
public sealed class StoreReadinessGate : IStoreReadinessGate
{
    private readonly TaskCompletionSource _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitAsync(CancellationToken ct = default) =>
        ct.CanBeCanceled ? _ready.Task.WaitAsync(ct) : _ready.Task;

    public void MarkReady() => _ready.TrySetResult();
}
