namespace Animora.Client.Services.Sync;

/// <summary>Abstracts waiting so schedulers are testable without real time.</summary>
public interface IDelayProvider
{
    /// <summary>Completes after <paramref name="delay"/>, or throws <see cref="OperationCanceledException"/> when cancelled.</summary>
    Task Delay(TimeSpan delay, CancellationToken ct = default);
}

/// <summary>Production delay backed by <see cref="TimeProvider"/> (system time by default).</summary>
public sealed class TimeProviderDelayProvider(TimeProvider? timeProvider = null) : IDelayProvider
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>Waits on the injected time provider.</summary>
    public Task Delay(TimeSpan delay, CancellationToken ct = default) => Task.Delay(delay, _time, ct);
}
